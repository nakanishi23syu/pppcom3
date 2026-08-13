# tools/dicom_test_data_generator/ — 検証用DICOMデータ生成スクリプト

会社の検証環境で使われている「pydicomで入力フォルダの元画像を読み込み、タグを書き換えたり
Series/SOPをたくさん作ったりして試験データを量産する」というワークフローを参考に作った、
このプロジェクト用の検証データ生成ツール群です。目的ごとにスクリプトを分けてあるので、
やりたいことに応じて使い分けてください。

> `tools/`配下は今後も別の検証用ツールが増える想定のため、直下に散らかさず、ツールごとに
> このようなサブフォルダを1つ作って、関連ファイル（スクリプト・入力データ・出力先）を
> すべてその中に収める方針にしています。

## フォルダ構成

```text
tools/dicom_test_data_generator/
├── README.md            … このファイル
├── requirements.txt      … 必要なPythonパッケージ
├── _common.py            … 各スクリプトが共有するヘルパー（直接実行しない）
├── dicom_tag_editor.py
├── dicom_study_builder.py
├── dicom_bulk_patient_generator.py
├── input/                … コピー元の元画像を置く場所（サンプルを同梱済み）
└── output/               … 生成結果の出力先（.gitignore対象、実行すると自動作成される）
```

## セットアップ

Python 3.9以降推奨。

```bash
pip install -r tools/dicom_test_data_generator/requirements.txt
```

## 共通の考え方

- 「`input/`（コピー元の元画像）」→ スクリプトで加工 → 「`output/`（生成結果）」という
  流れは全スクリプト共通。
- 元画像は自作しなくても、このフォルダに同梱されているサンプルを使えます。
  - `tools/dicom_test_data_generator/input/sample1.dcm`
  - `tools/dicom_test_data_generator/input/sample2.dcm`
  （どちらも`services/DicomTool.DicomScp/SampleData/`にあるものと同一のコピーです。
  このツール一式だけで完結させるため、あえて重複して置いています。）
- 各スクリプトが共通で使う処理（ファイル読み書き・タグ反映・UID発行・Study/Series/SOP
  組み立て）は `_common.py` にまとめてあります。これは直接実行するものではなく、他の
  スクリプトから import されるヘルパーです。
- **CLI引数(argparse)は使っていません。** 各スクリプトの`main()`関数の先頭に
  「ここを自由に書き換えてください」という区切りコメントで囲まれたパラメータ一覧が
  あるので、そこだけをエディタで書き換えてから、そのままファイルを実行してください。

  ```bash
  python tools/dicom_test_data_generator/スクリプト名.py
  ```

  パラメータ以降の部分（`run_xxx()`関数や`_common.py`）は、よほど動作自体を変えたい
  時以外は触らなくてよい作りにしてあります。
- 各スクリプトは、リポジトリのどこからでも上記の形で実行できます
  （`_common.py`と同じフォルダにあるスクリプト自身がimport元を解決するため、
  `python -m tools.dicom_test_data_generator.xxx`のような`-m`実行はしないでください）。

## スクリプト一覧

### 1. `dicom_tag_editor.py` — 指定したタグを書き換えて量産する

入力フォルダの各ファイルについて、指定したタグだけを書き換えて出力フォルダにコピーします。
「同じ元画像だけど患者IDやモダリティだけ変えたパターンを何個も作りたい」時に使います。

`main()`内で書き換えられるパラメータ:

| パラメータ | 内容 |
| --- | --- |
| `input_dir` / `output_dir` | 入力・出力フォルダ |
| `tag_overrides` | 書き換えたいタグの辞書（`{"PatientID": "TESTP001", ...}`） |
| `regenerate_uids` | `True`にするとStudy/Series/SOPのUIDを新規発行する |

`regenerate_uids = False`のままだと、同じ元ファイルを複数回流し込んでもUIDが同じままなので、
dicom-tool-3側では別レコードとして登録されず上書き（upsert）されます（`docs/CONTRACT.md`
7章のupsert仕様どおり）。「別レコードとして増やしたい」場合は`True`にしてください。

### 2. `dicom_study_builder.py` — 1人の患者に大量のSeries/SOPをぶら下げる

1つのテンプレートファイルを元に、1つのStudy配下にSeriesとSOPをまとめて量産します。
Worklist上での複数シリーズ表示や、Viewerでのシリーズ切り替え動作を検証したい時に使います。

`main()`内で書き換えられるパラメータ:

| パラメータ | 内容 |
| --- | --- |
| `template` | 元にする1枚のDICOMファイル |
| `output_dir` | 出力フォルダ |
| `series_count` / `instances_per_series` | Series数 × Series内のInstance数 |
| `patient_id` / `patient_name` / `study_description` | 患者・Study情報 |
| `modalities` | Seriesごとに順番に割り当てるモダリティのリスト |

### 3. `dicom_bulk_patient_generator.py` — 患者・Studyそのものを大量生成する

患者ごとに別々のStudyを大量に生成します。Worklistの件数を増やして、ページング・ソート・
検索・表示パフォーマンスを検証したい時に使います。

`main()`内で書き換えられるパラメータ:

| パラメータ | 内容 |
| --- | --- |
| `template` / `output_dir` | 元ファイルと出力フォルダ |
| `patient_count` | 生成する患者数 |
| `series_per_patient` / `instances_per_series` | 患者1人あたりのSeries数・Instance数 |
| `use_random_patient_id` | `True`なら`uuid`ベースのランダムID、`False`なら連番 |
| `patient_id_prefix` | 連番方式の時の接頭辞（例: `BULK` → `BULK0001`） |

## 生成したファイルの取り込み方

生成したファイルをdicom-tool-3に取り込むには、以下のいずれかを使ってください。

- Worklist（`frontend/worklist`）のアップロード画面からドラッグ&ドロップする
  （内部的にはGraphQLの`uploadDicomFiles` Mutation、HTTPマルチパート経由）。
- DICOM C-STORE（DIMSE）で`DicomTool.DicomScp`（ポート11112）に送信する
  （疎通確認用のSCUサンプルは`services/DicomTool.DicomScp/Services/DicomScuTestService.cs`参照）。

## 出力先について

`output/`配下は生成される使い捨てデータのため、`.gitignore`で除外しています。
