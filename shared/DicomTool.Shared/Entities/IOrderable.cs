namespace DicomTool.Shared.Entities;

// ======================================================
// IOrderable — 「並べ替え可能」であることを表すインターフェース
// ======================================================
// UserStudy / UserSeries / UserSop の3つは、それぞれ違うプロパティ名の
// UID（StudyInstanceUid等）を持つが、「並べ替え保存」の処理自体は
// 「UIDで引き当てて、渡された順番でOrderを書き換える」という共通のロジックでできる。
// GraphQL側の並べ替えMutationはこのインターフェース越しに1つの実装で済ませられる。
//
// ReorderKey はGraphQLのスキーマには公開したくない内部専用の値なので、
// 明示的インターフェース実装（`string IOrderable.ReorderKey => ...`）にして
// クラス自身の公開メンバーには出さないようにしている。
public interface IOrderable
{
    string ReorderKey { get; }
    int Order { get; set; }
}
