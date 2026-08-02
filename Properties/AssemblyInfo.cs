using System.Collections.Generic;
using WinRT;

// 让 CsWinRT 源生成器为这些 BCL 泛型集合生成 CCW vtable，
// 否则在 AOT 兼容项目（PublishAot=true）中，把它们作为 IEnumerable<string>
// 传入 WinRT API（如 WebView.InvokeScriptAsync 的 IIterable<String> 参数）时，
// 会抛出 InvalidCastException: Failed to create a CCW ... the specified cast is not valid。
[assembly: GeneratedWinRTExposedExternalType(typeof(List<string>))]
