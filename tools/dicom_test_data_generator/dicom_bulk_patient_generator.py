"""
dicom_bulk_patient_generator.py
=================================
1つのテンプレートDICOMファイルを元に、患者・Studyが異なる試験データを大量に生成するツール。
Worklistの「大量件数表示」時のページング・ソート・検索・表示パフォーマンスを検証したい
場合に使う。

【使い方】
下にある main() 内の「ここを自由に書き換えてください」ブロックだけを目的に合わせて
書き換えて、このファイルをそのまま実行する。
    python tools/dicom_test_data_generator/dicom_bulk_patient_generator.py

実際の処理（患者ごとのStudy組み立て・保存）は run_bulk_generate() と _common.py 側の
build_study() に切り出してあるので、パラメータブロック以外は触らなくてよい作りにしてある。
"""

import sys

from _common import build_study, generate_random_id, read_dataset, save_dataset


def main():
    # ==================== ここを自由に書き換えてください ====================
    template = "tools/dicom_test_data_generator/input/sample1.dcm"
    output_dir = "tools/dicom_test_data_generator/output/bulk"

    patient_count = 200
    series_per_patient = 1
    instances_per_series = 1

    # PatientIDの決め方。
    # False: 連番にする（patient_id_prefix + 4桁連番。例: BULK0001, BULK0002, ...）
    # True : 毎回重複しないランダムなIDにする（uuidベース。例: PT3F9A21B4）
    use_random_patient_id = False
    patient_id_prefix = "BULK"
    # ======================================================================

    run_bulk_generate(
        template,
        output_dir,
        patient_count,
        series_per_patient,
        instances_per_series,
        use_random_patient_id,
        patient_id_prefix,
    )


def make_patient_id(index, use_random, prefix):
    """1人分のPatientIDを決める。use_random=Trueならuuidベース、Falseなら連番。"""
    if use_random:
        return generate_random_id(prefix="PT")
    return f"{prefix}{index:04d}"


def run_bulk_generate(
    template,
    output_dir,
    patient_count,
    series_per_patient,
    instances_per_series,
    use_random_patient_id,
    patient_id_prefix,
):
    """patient_count人分の、それぞれ別患者・別StudyのDICOMデータを生成して保存する。"""
    template_ds = read_dataset(template)
    total_files = 0

    for i in range(1, patient_count + 1):
        patient_id = make_patient_id(i, use_random_patient_id, patient_id_prefix)
        patient_name = f"テスト患者{i:04d}"
        datasets = build_study(
            template_ds,
            series_count=series_per_patient,
            instances_per_series=instances_per_series,
            patient_id=patient_id,
            patient_name=patient_name,
            study_description=f"一括生成テスト {patient_id}",
        )
        patient_output_dir = f"{output_dir}/{patient_id}"
        for ds in datasets:
            save_dataset(ds, patient_output_dir)
        total_files += len(datasets)
        if i % 20 == 0 or i == patient_count:
            print(f"{i}/{patient_count} 人分生成済み...")

    print(f"完了: 患者{patient_count}人分、計{total_files}ファイルを {output_dir} に出力しました。")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    main()
