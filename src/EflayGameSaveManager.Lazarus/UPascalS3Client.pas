unit UPascalS3Client;

{$mode objfpc}{$H+}

interface

uses
  Classes, SysUtils, fpjson;

type
  TS3Backend = record
    Endpoint: string;
    Bucket: string;
    Region: string;
    AccessKeyId: string;
    SecretAccessKey: string;
  end;

  TPascalS3Client = class
  public
    class function TryDownloadUtf8String(
      const Backend: TS3Backend;
      const ObjectKey: string;
      out Content: string;
      out StatusCode: Integer): Boolean; static;
    class function TryDownloadFile(
      const Backend: TS3Backend;
      const ObjectKey: string;
      const DestinationPath: string;
      out ErrorContent: string;
      out StatusCode: Integer): Boolean; static;
    class function TryUploadFile(
      const Backend: TS3Backend;
      const ObjectKey: string;
      const SourcePath: string;
      out ErrorContent: string;
      out StatusCode: Integer): Boolean; static;
    class function TryUploadUtf8String(
      const Backend: TS3Backend;
      const ObjectKey: string;
      const Content: string;
      out ErrorContent: string;
      out StatusCode: Integer): Boolean; static;
  end;

function BuildGameBackupsKey(const CloudSettings: TJSONObject; const GameName: string): string;
function ReadS3Backend(const CloudSettings: TJSONObject): TS3Backend;

implementation

uses
  DateUtils, fphttpclient, URIParser, USha256;

const
  EmptyPayloadHash = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855';
  SigLineBreak = #10;

function NormalizeRootPath(const Value: string): string;
begin
  Result := StringReplace(Trim(Value), '\', '/', [rfReplaceAll]);
  while (Length(Result) > 0) and (Result[1] = '/') do
    Delete(Result, 1, 1);
  while (Length(Result) > 0) and (Result[Length(Result)] = '/') do
    Delete(Result, Length(Result), 1);
end;

function CombineKey(const Segments: array of string): string;
var
  Segment: string;
  Normalized: string;
begin
  Result := '';
  for Segment in Segments do
  begin
    Normalized := NormalizeRootPath(Segment);
    if Normalized = '' then
      Continue;
    if Result <> '' then
      Result += '/';
    Result += Normalized;
  end;
end;

function IsUnreserved(B: Byte): Boolean; inline;
begin
  Result := ((B >= Ord('A')) and (B <= Ord('Z'))) or
            ((B >= Ord('a')) and (B <= Ord('z'))) or
            ((B >= Ord('0')) and (B <= Ord('9'))) or
            (B = Ord('-')) or (B = Ord('_')) or (B = Ord('.')) or (B = Ord('~'));
end;

function PercentEncode(const Value: string): string;
const
  HexChars: array[0..15] of Char = '0123456789ABCDEF';
var
  Bytes: RawByteString;
  I: Integer;
  B: Byte;
begin
  Bytes := Value;
  SetCodePage(Bytes, CP_NONE, False);
  Result := '';
  for I := 1 to Length(Bytes) do
  begin
    B := Byte(Bytes[I]);
    if IsUnreserved(B) then
      Result += Char(B)
    else
      Result += '%' + HexChars[B shr 4] + HexChars[B and $f];
  end;
end;

function BuildCanonicalUri(const EndpointUri: TURI; const Bucket, ObjectKey: string): string;
var
  Parts: TStringList;
  BasePath: string;
  Segments: TStringArray;
  Segment: string;
  I: Integer;
begin
  Parts := TStringList.Create;
  try
    BasePath := NormalizeRootPath(EndpointUri.Path);
    if BasePath <> '' then
    begin
      Segments := BasePath.Split('/');
      for Segment in Segments do
        if Segment <> '' then
          Parts.Add(Segment);
    end;

    Parts.Add(Bucket);
    Segments := NormalizeRootPath(ObjectKey).Split('/');
    for Segment in Segments do
      if Segment <> '' then
        Parts.Add(Segment);

    Result := '';
    for I := 0 to Parts.Count - 1 do
      Result += '/' + PercentEncode(Parts[I]);
    if Result = '' then
      Result := '/';
  finally
    Parts.Free;
  end;
end;

function BuildHostHeader(const EndpointUri: TURI): string;
var
  DefaultPort: Boolean;
begin
  Result := EndpointUri.Host;
  DefaultPort := ((EndpointUri.Protocol = 'http') and (EndpointUri.Port = 80)) or
                 ((EndpointUri.Protocol = 'https') and (EndpointUri.Port = 443)) or
                 (EndpointUri.Port = 0);
  if not DefaultPort then
    Result += ':' + IntToStr(EndpointUri.Port);
end;

function BuildRequestUrl(const EndpointUri: TURI; const CanonicalUri: string): string;
begin
  Result := EndpointUri.Protocol + '://' + BuildHostHeader(EndpointUri) + CanonicalUri;
end;

function AmzTimestamp(out ShortDate: string): string;
var
  UtcNow: TDateTime;
begin
  UtcNow := LocalTimeToUniversal(Now);
  ShortDate := FormatDateTime('yyyymmdd', UtcNow);
  Result := FormatDateTime('yyyymmdd"T"hhnnss"Z"', UtcNow);
end;

function CreateSignature(const SecretAccessKey, ShortDate, Region, StringToSign: string): string;
var
  DateKey: RawByteString;
  RegionKey: RawByteString;
  ServiceKey: RawByteString;
  SigningKey: RawByteString;
begin
  DateKey := DigestToRawString(HmacSha256Bytes(UTF8String('AWS4' + SecretAccessKey), UTF8String(ShortDate)));
  RegionKey := DigestToRawString(HmacSha256Bytes(DateKey, UTF8String(Region)));
  ServiceKey := DigestToRawString(HmacSha256Bytes(RegionKey, 's3'));
  SigningKey := DigestToRawString(HmacSha256Bytes(ServiceKey, 'aws4_request'));
  Result := HmacSha256Hex(SigningKey, UTF8String(StringToSign));
end;

function LoadFileBytes(const FilePath: string): RawByteString;
var
  Stream: TFileStream;
begin
  Stream := TFileStream.Create(FilePath, fmOpenRead or fmShareDenyWrite);
  try
    SetLength(Result, Stream.Size);
    SetCodePage(Result, CP_NONE, False);
    if Stream.Size > 0 then
      Stream.ReadBuffer(Result[1], Stream.Size);
  finally
    Stream.Free;
  end;
end;

function StreamToRawString(const Stream: TStream): RawByteString;
begin
  SetLength(Result, Stream.Size);
  SetCodePage(Result, CP_NONE, False);
  if Stream.Size > 0 then
  begin
    Stream.Position := 0;
    Stream.ReadBuffer(Result[1], Stream.Size);
  end;
end;

function TryUploadPayload(
  const Backend: TS3Backend;
  const ObjectKey: string;
  const Body: RawByteString;
  out ErrorContent: string;
  out StatusCode: Integer): Boolean;
var
  EndpointUri: TURI;
  CanonicalUri: string;
  RequestUrl: string;
  HostHeader: string;
  ShortDate: string;
  AmzDate: string;
  PayloadHash: string;
  CredentialScope: string;
  CanonicalHeaders: string;
  SignedHeaders: string;
  CanonicalRequest: string;
  StringToSign: string;
  Signature: string;
  Client: TFPHTTPClient;
  Request: TRawByteStringStream;
  Response: TMemoryStream;
begin
  Result := False;
  ErrorContent := '';
  StatusCode := 0;

  EndpointUri := ParseURI(Backend.Endpoint);
  CanonicalUri := BuildCanonicalUri(EndpointUri, Backend.Bucket, ObjectKey);
  RequestUrl := BuildRequestUrl(EndpointUri, CanonicalUri);
  HostHeader := BuildHostHeader(EndpointUri);
  AmzDate := AmzTimestamp(ShortDate);
  PayloadHash := Sha256Hex(Body);
  CredentialScope := ShortDate + '/' + Backend.Region + '/s3/aws4_request';
  SignedHeaders := 'host;x-amz-content-sha256;x-amz-date';
  CanonicalHeaders := 'host:' + HostHeader + SigLineBreak +
                      'x-amz-content-sha256:' + PayloadHash + SigLineBreak +
                      'x-amz-date:' + AmzDate + SigLineBreak;
  CanonicalRequest := 'PUT' + SigLineBreak +
                      CanonicalUri + SigLineBreak +
                      '' + SigLineBreak +
                      CanonicalHeaders + SigLineBreak +
                      SignedHeaders + SigLineBreak +
                      PayloadHash;
  StringToSign := 'AWS4-HMAC-SHA256' + SigLineBreak +
                  AmzDate + SigLineBreak +
                  CredentialScope + SigLineBreak +
                  Sha256Hex(UTF8String(CanonicalRequest));
  Signature := CreateSignature(Backend.SecretAccessKey, ShortDate, Backend.Region, StringToSign);

  Client := TFPHTTPClient.Create(nil);
  Request := TRawByteStringStream.Create(Body);
  Response := TMemoryStream.Create;
  try
    Client.AllowRedirect := False;
    Client.RequestBody := Request;
    Client.AddHeader('Accept-Encoding', 'identity');
    Client.AddHeader('x-amz-content-sha256', PayloadHash);
    Client.AddHeader('x-amz-date', AmzDate);
    Client.AddHeader(
      'Authorization',
      'AWS4-HMAC-SHA256 Credential=' + Backend.AccessKeyId + '/' + CredentialScope +
      ', SignedHeaders=' + SignedHeaders + ', Signature=' + Signature);
    try
      Client.HTTPMethod('PUT', RequestUrl, Response, [200, 204]);
      StatusCode := Client.ResponseStatusCode;
      Result := (StatusCode = 200) or (StatusCode = 204);
      if not Result then
        ErrorContent := StreamToRawString(Response);
    except
      on E: EHTTPClient do
      begin
        StatusCode := Client.ResponseStatusCode;
        ErrorContent := E.Message;
        Result := False;
      end;
    end;
  finally
    Client.RequestBody := nil;
    Response.Free;
    Request.Free;
    Client.Free;
  end;
end;

class function TPascalS3Client.TryUploadUtf8String(
  const Backend: TS3Backend;
  const ObjectKey: string;
  const Content: string;
  out ErrorContent: string;
  out StatusCode: Integer): Boolean;
var
  Body: RawByteString;
begin
  Body := UTF8String(Content);
  Result := TryUploadPayload(Backend, ObjectKey, Body, ErrorContent, StatusCode);
end;

class function TPascalS3Client.TryUploadFile(
  const Backend: TS3Backend;
  const ObjectKey: string;
  const SourcePath: string;
  out ErrorContent: string;
  out StatusCode: Integer): Boolean;
begin
  Result := TryUploadPayload(Backend, ObjectKey, LoadFileBytes(SourcePath), ErrorContent, StatusCode);
end;

function ReadS3Backend(const CloudSettings: TJSONObject): TS3Backend;
var
  Backend: TJSONObject;
begin
  Backend := CloudSettings.Objects['backend'];
  if Backend = nil then
    raise Exception.Create('Missing cloud backend settings.');

  if not SameText(Backend.Get('type', ''), 'S3') then
    raise Exception.Create('Only S3 cloud backend is supported.');

  Result.Endpoint := Backend.Get('endpoint', '');
  Result.Bucket := Backend.Get('bucket', '');
  Result.Region := Backend.Get('region', '');
  if Result.Region = '' then
    Result.Region := 'us-east-1';
  Result.AccessKeyId := Backend.Get('access_key_id', '');
  Result.SecretAccessKey := Backend.Get('secret_access_key', '');

  if (Result.Endpoint = '') or (Result.Bucket = '') or
     (Result.AccessKeyId = '') or (Result.SecretAccessKey = '') then
    raise Exception.Create('Cloud backend configuration is incomplete.');
end;

function BuildGameBackupsKey(const CloudSettings: TJSONObject; const GameName: string): string;
begin
  Result := CombineKey([CloudSettings.Get('root_path', ''), 'save_data', GameName, 'Backups.json']);
end;

class function TPascalS3Client.TryDownloadUtf8String(
  const Backend: TS3Backend;
  const ObjectKey: string;
  out Content: string;
  out StatusCode: Integer): Boolean;
var
  EndpointUri: TURI;
  CanonicalUri: string;
  RequestUrl: string;
  HostHeader: string;
  ShortDate: string;
  AmzDate: string;
  CredentialScope: string;
  CanonicalHeaders: string;
  SignedHeaders: string;
  CanonicalRequest: string;
  StringToSign: string;
  Signature: string;
  Client: TFPHTTPClient;
  Response: TRawByteStringStream;
begin
  Result := False;
  Content := '';
  StatusCode := 0;

  EndpointUri := ParseURI(Backend.Endpoint);
  CanonicalUri := BuildCanonicalUri(EndpointUri, Backend.Bucket, ObjectKey);
  RequestUrl := BuildRequestUrl(EndpointUri, CanonicalUri);
  HostHeader := BuildHostHeader(EndpointUri);
  AmzDate := AmzTimestamp(ShortDate);
  CredentialScope := ShortDate + '/' + Backend.Region + '/s3/aws4_request';
  SignedHeaders := 'host;x-amz-content-sha256;x-amz-date';
  CanonicalHeaders := 'host:' + HostHeader + SigLineBreak +
                      'x-amz-content-sha256:' + EmptyPayloadHash + SigLineBreak +
                      'x-amz-date:' + AmzDate + SigLineBreak;
  CanonicalRequest := 'GET' + SigLineBreak +
                      CanonicalUri + SigLineBreak +
                      '' + SigLineBreak +
                      CanonicalHeaders + SigLineBreak +
                      SignedHeaders + SigLineBreak +
                      EmptyPayloadHash;
  StringToSign := 'AWS4-HMAC-SHA256' + SigLineBreak +
                  AmzDate + SigLineBreak +
                  CredentialScope + SigLineBreak +
                  Sha256Hex(UTF8String(CanonicalRequest));
  Signature := CreateSignature(Backend.SecretAccessKey, ShortDate, Backend.Region, StringToSign);

  Client := TFPHTTPClient.Create(nil);
  Response := TRawByteStringStream.Create('');
  try
    Client.AllowRedirect := False;
    Client.AddHeader('Accept-Encoding', 'identity');
    Client.AddHeader('x-amz-content-sha256', EmptyPayloadHash);
    Client.AddHeader('x-amz-date', AmzDate);
    Client.AddHeader(
      'Authorization',
      'AWS4-HMAC-SHA256 Credential=' + Backend.AccessKeyId + '/' + CredentialScope +
      ', SignedHeaders=' + SignedHeaders + ', Signature=' + Signature);
    try
      Client.HTTPMethod('GET', RequestUrl, Response, [200, 400, 403, 404]);
      StatusCode := Client.ResponseStatusCode;
      Content := Response.DataString;
      Result := StatusCode = 200;
    except
      on E: EHTTPClient do
      begin
        StatusCode := Client.ResponseStatusCode;
        Content := E.Message;
        Result := False;
      end;
    end;
  finally
    Response.Free;
    Client.Free;
  end;
end;

class function TPascalS3Client.TryDownloadFile(
  const Backend: TS3Backend;
  const ObjectKey: string;
  const DestinationPath: string;
  out ErrorContent: string;
  out StatusCode: Integer): Boolean;
var
  EndpointUri: TURI;
  CanonicalUri: string;
  RequestUrl: string;
  HostHeader: string;
  ShortDate: string;
  AmzDate: string;
  CredentialScope: string;
  CanonicalHeaders: string;
  SignedHeaders: string;
  CanonicalRequest: string;
  StringToSign: string;
  Signature: string;
  Client: TFPHTTPClient;
  Response: TMemoryStream;
  Destination: TFileStream;
begin
  Result := False;
  ErrorContent := '';
  StatusCode := 0;

  EndpointUri := ParseURI(Backend.Endpoint);
  CanonicalUri := BuildCanonicalUri(EndpointUri, Backend.Bucket, ObjectKey);
  RequestUrl := BuildRequestUrl(EndpointUri, CanonicalUri);
  HostHeader := BuildHostHeader(EndpointUri);
  AmzDate := AmzTimestamp(ShortDate);
  CredentialScope := ShortDate + '/' + Backend.Region + '/s3/aws4_request';
  SignedHeaders := 'host;x-amz-content-sha256;x-amz-date';
  CanonicalHeaders := 'host:' + HostHeader + SigLineBreak +
                      'x-amz-content-sha256:' + EmptyPayloadHash + SigLineBreak +
                      'x-amz-date:' + AmzDate + SigLineBreak;
  CanonicalRequest := 'GET' + SigLineBreak +
                      CanonicalUri + SigLineBreak +
                      '' + SigLineBreak +
                      CanonicalHeaders + SigLineBreak +
                      SignedHeaders + SigLineBreak +
                      EmptyPayloadHash;
  StringToSign := 'AWS4-HMAC-SHA256' + SigLineBreak +
                  AmzDate + SigLineBreak +
                  CredentialScope + SigLineBreak +
                  Sha256Hex(UTF8String(CanonicalRequest));
  Signature := CreateSignature(Backend.SecretAccessKey, ShortDate, Backend.Region, StringToSign);

  Client := TFPHTTPClient.Create(nil);
  Response := TMemoryStream.Create;
  try
    Client.AllowRedirect := False;
    Client.AddHeader('Accept-Encoding', 'identity');
    Client.AddHeader('x-amz-content-sha256', EmptyPayloadHash);
    Client.AddHeader('x-amz-date', AmzDate);
    Client.AddHeader(
      'Authorization',
      'AWS4-HMAC-SHA256 Credential=' + Backend.AccessKeyId + '/' + CredentialScope +
      ', SignedHeaders=' + SignedHeaders + ', Signature=' + Signature);
    try
      Client.HTTPMethod('GET', RequestUrl, Response, [200, 400, 403, 404]);
      StatusCode := Client.ResponseStatusCode;
      if StatusCode = 200 then
      begin
        ForceDirectories(ExtractFileDir(DestinationPath));
        Response.Position := 0;
        Destination := TFileStream.Create(DestinationPath, fmCreate);
        try
          Destination.CopyFrom(Response, 0);
        finally
          Destination.Free;
        end;
        Result := True;
      end
      else
      begin
        SetLength(ErrorContent, Response.Size);
        if Response.Size > 0 then
        begin
          Response.Position := 0;
          Response.ReadBuffer(ErrorContent[1], Response.Size);
        end;
        Result := False;
      end;
    except
      on E: EHTTPClient do
      begin
        StatusCode := Client.ResponseStatusCode;
        ErrorContent := E.Message;
        Result := False;
      end;
    end;
  finally
    Response.Free;
    Client.Free;
  end;
end;

end.
