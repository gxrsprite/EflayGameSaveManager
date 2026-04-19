unit USha256;

{$mode objfpc}{$H+}

interface

type
  TSha256Digest = array[0..31] of Byte;

function Sha256Bytes(const Data: RawByteString): TSha256Digest;
function Sha256Hex(const Data: RawByteString): string;
function HmacSha256Bytes(const Key, Data: RawByteString): TSha256Digest;
function HmacSha256Hex(const Key, Data: RawByteString): string;
function DigestToHex(const Digest: TSha256Digest): string;
function DigestToRawString(const Digest: TSha256Digest): RawByteString;

implementation

uses
  SysUtils;

const
  K: array[0..63] of DWord = (
    $428a2f98, $71374491, $b5c0fbcf, $e9b5dba5, $3956c25b, $59f111f1, $923f82a4, $ab1c5ed5,
    $d807aa98, $12835b01, $243185be, $550c7dc3, $72be5d74, $80deb1fe, $9bdc06a7, $c19bf174,
    $e49b69c1, $efbe4786, $0fc19dc6, $240ca1cc, $2de92c6f, $4a7484aa, $5cb0a9dc, $76f988da,
    $983e5152, $a831c66d, $b00327c8, $bf597fc7, $c6e00bf3, $d5a79147, $06ca6351, $14292967,
    $27b70a85, $2e1b2138, $4d2c6dfc, $53380d13, $650a7354, $766a0abb, $81c2c92e, $92722c85,
    $a2bfe8a1, $a81a664b, $c24b8b70, $c76c51a3, $d192e819, $d6990624, $f40e3585, $106aa070,
    $19a4c116, $1e376c08, $2748774c, $34b0bcb5, $391c0cb3, $4ed8aa4a, $5b9cca4f, $682e6ff3,
    $748f82ee, $78a5636f, $84c87814, $8cc70208, $90befffa, $a4506ceb, $bef9a3f7, $c67178f2);

function RotateRight(Value: DWord; Bits: Byte): DWord; inline;
begin
  Result := (Value shr Bits) or (Value shl (32 - Bits));
end;

function Ch(X, Y, Z: DWord): DWord; inline;
begin
  Result := (X and Y) xor ((not X) and Z);
end;

function Maj(X, Y, Z: DWord): DWord; inline;
begin
  Result := (X and Y) xor (X and Z) xor (Y and Z);
end;

function BigSigma0(X: DWord): DWord; inline;
begin
  Result := RotateRight(X, 2) xor RotateRight(X, 13) xor RotateRight(X, 22);
end;

function BigSigma1(X: DWord): DWord; inline;
begin
  Result := RotateRight(X, 6) xor RotateRight(X, 11) xor RotateRight(X, 25);
end;

function SmallSigma0(X: DWord): DWord; inline;
begin
  Result := RotateRight(X, 7) xor RotateRight(X, 18) xor (X shr 3);
end;

function SmallSigma1(X: DWord): DWord; inline;
begin
  Result := RotateRight(X, 17) xor RotateRight(X, 19) xor (X shr 10);
end;

function Sha256Bytes(const Data: RawByteString): TSha256Digest;
var
  H: array[0..7] of DWord;
  W: array[0..63] of DWord;
  A, B, C, D, E, F, G, HH: DWord;
  T1, T2: DWord;
  BitLen: QWord;
  Input: RawByteString;
  Padded: RawByteString;
  NewLen: SizeInt;
  Offset, I, J: SizeInt;
begin
  H[0] := $6a09e667;
  H[1] := $bb67ae85;
  H[2] := $3c6ef372;
  H[3] := $a54ff53a;
  H[4] := $510e527f;
  H[5] := $9b05688c;
  H[6] := $1f83d9ab;
  H[7] := $5be0cd19;

  Input := Data;
  SetCodePage(Input, CP_NONE, False);

  BitLen := QWord(Length(Input)) * 8;
  NewLen := Length(Input) + 1;
  while (NewLen mod 64) <> 56 do
    Inc(NewLen);

  SetLength(Padded, NewLen + 8);
  SetCodePage(Padded, CP_NONE, False);
  FillChar(Padded[1], Length(Padded), 0);
  if Length(Input) > 0 then
    Move(Input[1], Padded[1], Length(Input));
  Padded[Length(Input) + 1] := #$80;

  for I := 0 to 7 do
    Padded[NewLen + 8 - I] := Char((BitLen shr (I * 8)) and $ff);

  Offset := 1;
  while Offset <= Length(Padded) do
  begin
    for I := 0 to 15 do
    begin
      J := Offset + (I * 4);
      W[I] := (DWord(Byte(Padded[J])) shl 24) or
              (DWord(Byte(Padded[J + 1])) shl 16) or
              (DWord(Byte(Padded[J + 2])) shl 8) or
              DWord(Byte(Padded[J + 3]));
    end;

    for I := 16 to 63 do
      W[I] := SmallSigma1(W[I - 2]) + W[I - 7] + SmallSigma0(W[I - 15]) + W[I - 16];

    A := H[0];
    B := H[1];
    C := H[2];
    D := H[3];
    E := H[4];
    F := H[5];
    G := H[6];
    HH := H[7];

    for I := 0 to 63 do
    begin
      T1 := HH + BigSigma1(E) + Ch(E, F, G) + K[I] + W[I];
      T2 := BigSigma0(A) + Maj(A, B, C);
      HH := G;
      G := F;
      F := E;
      E := D + T1;
      D := C;
      C := B;
      B := A;
      A := T1 + T2;
    end;

    H[0] += A;
    H[1] += B;
    H[2] += C;
    H[3] += D;
    H[4] += E;
    H[5] += F;
    H[6] += G;
    H[7] += HH;

    Inc(Offset, 64);
  end;

  for I := 0 to 7 do
  begin
    Result[I * 4] := (H[I] shr 24) and $ff;
    Result[I * 4 + 1] := (H[I] shr 16) and $ff;
    Result[I * 4 + 2] := (H[I] shr 8) and $ff;
    Result[I * 4 + 3] := H[I] and $ff;
  end;
end;

function DigestToHex(const Digest: TSha256Digest): string;
const
  HexChars: array[0..15] of Char = '0123456789abcdef';
var
  I: Integer;
begin
  SetLength(Result, 64);
  for I := 0 to 31 do
  begin
    Result[(I * 2) + 1] := HexChars[Digest[I] shr 4];
    Result[(I * 2) + 2] := HexChars[Digest[I] and $f];
  end;
end;

function DigestToRawString(const Digest: TSha256Digest): RawByteString;
var
  I: Integer;
begin
  SetLength(Result, 32);
  for I := 0 to 31 do
    Result[I + 1] := Char(Digest[I]);
  SetCodePage(Result, CP_NONE, False);
end;

function Sha256Hex(const Data: RawByteString): string;
begin
  Result := DigestToHex(Sha256Bytes(Data));
end;

function HmacSha256Bytes(const Key, Data: RawByteString): TSha256Digest;
var
  BlockKey: RawByteString;
  NormalizedKey: RawByteString;
  InnerPad: RawByteString;
  OuterPad: RawByteString;
  Message: RawByteString;
  InnerHash: RawByteString;
  I: Integer;
begin
  BlockKey := Key;
  SetCodePage(BlockKey, CP_NONE, False);
  Message := Data;
  SetCodePage(Message, CP_NONE, False);
  if Length(BlockKey) > 64 then
    BlockKey := DigestToRawString(Sha256Bytes(BlockKey));

  SetLength(NormalizedKey, 64);
  SetCodePage(NormalizedKey, CP_NONE, False);
  FillChar(NormalizedKey[1], 64, 0);
  if Length(BlockKey) > 0 then
    Move(BlockKey[1], NormalizedKey[1], Length(BlockKey));
  SetLength(InnerPad, 64);
  SetLength(OuterPad, 64);
  SetCodePage(InnerPad, CP_NONE, False);
  SetCodePage(OuterPad, CP_NONE, False);

  for I := 1 to 64 do
  begin
    InnerPad[I] := Char(Byte(NormalizedKey[I]) xor $36);
    OuterPad[I] := Char(Byte(NormalizedKey[I]) xor $5c);
  end;

  InnerHash := DigestToRawString(Sha256Bytes(InnerPad + Message));
  Result := Sha256Bytes(OuterPad + InnerHash);
end;

function HmacSha256Hex(const Key, Data: RawByteString): string;
begin
  Result := DigestToHex(HmacSha256Bytes(Key, Data));
end;

end.
