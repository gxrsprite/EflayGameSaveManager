# Folder 与 ZIP 的统一层级规则

`Folder`、`Zip` 单元都保存并使用 `contents_only`。安卓新增/编辑界面与桌面编辑界面使用同一配置字段，ZIP 模式不隐藏或清除它。

| 输入文件夹或 ZIP 树 | `contents_only` | ZIP 恢复输出 |
| --- | --- | --- |
| `savedata/1/CUSA01740/AUTOSAVE/SAVEDATA` | `false` | `savedata/1/CUSA01740/AUTOSAVE/SAVEDATA` |
| `savedata/1/CUSA01740/AUTOSAVE/SAVEDATA` | `true` | `1/CUSA01740/AUTOSAVE/SAVEDATA` |

只处理一层。原始 ZIP 在根目录有文件或多个目录时，没有唯一外层文件夹可移除，内容保持原层级。外部 ZIP 的唯一顶层目录被视为它的外层文件夹；程序输出的内容 ZIP 则通过布局信息标明它已经去掉了外层，不能再次剥掉唯一业务子目录。

Folder 的目标路径本身就是目标文件夹。已有规则保持兼容：相同名称的归档外层匹配目标路径，不再套一层；Folder 内容模式忽略源文件夹名称。内部 `content` 标记不是用户文件夹名，不写进导出的 ZIP。

## 实现约定

- `ZipSaveLayoutService` 是 ZIP 上传、ZIP 恢复及本地 ZIP 备份的共同入口。源 ZIP 不原地修改；工作目录使用唯一子目录。
- 云端 ZIP 布局与 Folder 相同：`<unit-id>/<folder-name>/...` 或 `<unit-id>/content/...`。ZIP 内容使用普通 deflate 压缩，文件字节保持原样。
- `RGSM_ARCHIVE_V2` ZIP comment 的 JSON 新增 `units`，记录每个 Folder / ZIP 单元的原文件夹名和内容模式。只记录名称，不记录绝对路径。
- 用户可导入游戏的 ZIP 输出 comment 使用 `RGSM_ZIP_PAYLOAD_V1`，记录 `folder_name`、`contents_only`。因此即使内容根目录只剩 `1/`，重复同步也不会继续剥离。关闭开关时可重新带上原文件夹名。
- 老备份没有原文件夹名元数据时，支持旧 `0/<folder>/...` 和 `0/content/...` 布局。老 content 备份不能凭空恢复丢失的原文件夹名；导出带名称的 ZIP 时使用调用者提供的 ZIP 名称或游戏名称作为后备名称。
- 现代归档通过元数据区分真实名为 `content` 的文件夹与内部标记。ZIP 输出不包含单元编号。
- 每台设备只允许一个 ZIP 单元；混合多个当前设备单元不得静默只取其中一个。不同设备可以通过 `linked_unit_ids` 分别使用 Folder 与 ZIP。关联单元必须只使用各自设备显式路径，不能回退到另一平台。
- 空 ZIP 不允许覆盖已有存档。无效目录名称、布局与元数据不一致时中止处理。

## 验证

`ZipContentsOnlyTests` 覆盖 Folder→ZIP、ZIP→Folder、ZIP→ZIP 的开关组合，连续三次往返、开关切换、数字业务目录保留、本地 ZIP 备份、源文件不变、多根目录、关联设备路径、真实 `content` 文件夹及空 ZIP 保护。
