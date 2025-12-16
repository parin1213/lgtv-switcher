namespace LGTVSwitcher.Core;

/// <summary>
/// OS ごとのパス（設定・状態ファイルなど）を提供する。
/// </summary>
public interface IAppPathProvider
{
    /// <summary>
    /// デバイス状態を保存するファイルパス（ClientKey など）。
    /// </summary>
    string GetStateFilePath();

    /// <summary>
    /// ログを配置するディレクトリパス。
    /// </summary>
    string GetLogsDirectory();
}
