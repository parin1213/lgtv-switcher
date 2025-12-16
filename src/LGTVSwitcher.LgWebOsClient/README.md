# LGTVSwitcher.LgWebOsClient

LG webOS TV 向けのクライアント実装（発見 / 接続 / プロトコル解析）をまとめたプロジェクトです。

## フォルダ構成ポリシー

- `Discovery/` : TV の探索（SSDP など）。探索結果モデルやパーサーもここに置く。
- `Session/` : WebSocket の接続・登録（ペアリング）・再接続を扱うセッション層。
- `Protocol/` : JSON エンベロープ / 登録応答 / 各種レスポンスの解析・モデル定義。
- `Transport/` : 通信トランスポート（例: `ClientWebSocket` 実装、TLS 検証緩和など）。
- `Exceptions/` : クライアントが投げる例外型。

基本方針として、ルート直下には「利用者が最初に触る入口」以外を増やさず、追加実装は上記の関心ごと配下に寄せます。
