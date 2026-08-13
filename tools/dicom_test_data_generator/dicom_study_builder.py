"""
dicom_study_builder.py
========================
1つのテンプレートDICOMファイルを元に、1つのStudy配下にSeriesとSOPをまとめて量産するツール。
Worklist上での複数シリーズ表示や、Viewerでのシリーズ切り替え動作など、「Study/Seriesの
階層構造そのもの」を検証したい場合に使う。

【使い方】
下にある main() 内の「ここを自由に書き換えてください」ブロックだけを目的に合わせて
書き換えて、このファイルをそのまま実行する。
    python tools/dicom_test_data_generator/dicom_study_builder.py

実際の処理（Series/SOPの組み立て・保存）は run_study_builder() と _common.py 側の
build_study() に切り出してあるので、パラメータブロック以外は触らなくてよい作りにしてある。
"""

import sys

from _common import build_study, read_dataset, save_dataset


def main():
    # ==================== ここを自由に書き換えてください ====================
    template = "tools/dicom_test_data_generator/input/sample1.dcm"
    output_dir = "tools/dicom_test_data_generator/output/study1"

    series_count = 3
    instances_per_series = 5

    patient_id = "TESTP001"
    patient_name = "テスト 太郎"
    study_description = None  # Noneのままなら元画像の値をそのまま使う

    # Seriesごとに順番に割り当てるモダリティ（要素数がseries_countと合わなくても、
    # 余ったら先頭に戻って割り当てられる）。Noneのままなら元画像のModalityのまま。
    modalities = ["CT", "MR"]
    # ======================================================================

    run_study_builder(
        template,
        output_dir,
        series_count,
        instances_per_series,
        patient_id,
        patient_name,
        study_description,
        modalities,
    )


def run_study_builder(
    template,
    output_dir,
    series_count,
    instances_per_series,
    patient_id,
    patient_name,
    study_description,
    modalities,
):
    """テンプレートファイル1つから、指定した構成のStudyを組み立てて保存する。"""
    template_ds = read_dataset(template)
    datasets = build_study(
        template_ds,
        series_count=series_count,
        instances_per_series=instances_per_series,
        patient_id=patient_id,
        patient_name=patient_name,
        study_description=study_description,
        modalities=modalities,
    )

    for ds in datasets:
        save_dataset(ds, output_dir)

    print(
        f"完了: Series {series_count}件 x Instance {instances_per_series}件 "
        f"= 計{len(datasets)}ファイルを {output_dir} に出力しました。"
    )


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    main()
