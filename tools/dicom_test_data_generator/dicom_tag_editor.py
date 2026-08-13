"""
dicom_tag_editor.py
=====================
入力フォルダにあるDICOMファイルを読み込み、指定したタグを指定した値に書き換えて
出力フォルダへ保存するツール。「同じ元画像だけど患者IDやモダリティだけ変えたパターンを
何個も作りたい」といった用途で使う。

【使い方】
下にある main() 内の「ここを自由に書き換えてください」ブロックだけを目的に合わせて
書き換えて、このファイルをそのまま実行する。
    python tools/dicom_test_data_generator/dicom_tag_editor.py

実際の処理（ファイルの読み書き・タグの反映）は run_tag_edit() と _common.py 側の関数に
切り出してあるので、パラメータブロック以外は触らなくてよい作りにしてある。
"""

import sys

from _common import (
    apply_tag_overrides,
    find_dicom_files,
    read_dataset,
    regenerate_instance_uids,
    save_dataset,
)


def main():
    # ==================== ここを自由に書き換えてください ====================
    input_dir = "tools/dicom_test_data_generator/input"
    output_dir = "tools/dicom_test_data_generator/output/tag_edit"

    # 書き換えたいタグを キーワード: 値 の形で好きなだけ増減してよい。
    # キーワードはpydicomの標準DICOMキーワード（PatientID, PatientName, Modality,
    # StudyDescription, AccessionNumber, SeriesDescription 等）をそのまま使う。
    tag_overrides = {
        "PatientID": "TESTP001",
        "PatientName": "テスト 太郎",
        "Modality": "CT",
    }

    # Trueにすると、StudyInstanceUID/SeriesInstanceUID/SOPInstanceUIDをファイルごとに
    # 新規発行する。同じ元ファイルを何度も流し込んでdicom-tool-3側で別レコードとして
    # 登録させたい時はTrueにする（Falseだと同じUIDのまま＝upsertされて上書きになる）。
    regenerate_uids = True
    # ======================================================================

    run_tag_edit(input_dir, output_dir, tag_overrides, regenerate_uids)


def run_tag_edit(input_dir, output_dir, tag_overrides, regenerate_uids):
    """input_dir配下の全DICOMファイルにtag_overridesを適用し、output_dirへ保存する。"""
    files = find_dicom_files(input_dir)
    if not files:
        print(f"入力フォルダに.dcmファイルが見つかりませんでした: {input_dir}")
        return

    for path in files:
        ds = read_dataset(path)
        if regenerate_uids:
            regenerate_instance_uids(ds, new_study=True, new_series=True)
        apply_tag_overrides(ds, tag_overrides)
        output_path = save_dataset(ds, output_dir)
        print(f"{path.name} -> {output_path}")

    print(f"完了: {len(files)}件を {output_dir} に出力しました。")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    main()
