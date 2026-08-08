"""
generate_test_dicom.py
========================
pydicomを使って、E2Eテストのアップロード検証(test_upload.py)で使う
「最小限のダミーDICOMファイル」を1枚生成するユーティリティ。

本物の医療画像ではなく、中央に円を描いた256x256のグラデーション画像1枚だけを持つ、
DICOMとして最低限成立する形式のファイルを作る（fo-dicomでのタグ解析・
Study/Series/SOP登録の動作確認ができれば十分なため）。

【使い方】
- test_upload.py からは import して generate_sample_dicom() を直接呼び出す
  （tests/e2e/conftest.py の sample_dicom_file フィクスチャ経由）。
- 単体で動作確認したい場合は、このファイル単独でも実行できる:
    python tests/e2e/generate_test_dicom.py
  実行すると、環境変数 OUTPUT_DIR（未設定時は tests/e2e/output/）にファイルを1つ生成する。
"""

import datetime
import os

import numpy as np
import pydicom
from pydicom.dataset import FileDataset, FileMetaDataset
from pydicom.uid import ExplicitVRLittleEndian, SecondaryCaptureImageStorage, generate_uid

DEFAULT_OUTPUT_DIR = os.environ.get(
    "OUTPUT_DIR", os.path.join(os.path.dirname(os.path.abspath(__file__)), "output")
)


def generate_sample_dicom(
    output_path=None,
    patient_id="patient-pytest-001",
    patient_name="テスト 太郎",
    study_description="Claude Code Python生成テスト",
    modality="MR",
):
    """
    最小限のテスト用DICOMファイルを1つ生成して保存する。

    Args:
        output_path: 保存先のフルパス。省略時は DEFAULT_OUTPUT_DIR 配下に、
            StudyInstanceUIDを使ったファイル名で保存する。
        patient_id / patient_name / study_description / modality:
            DICOMタグに書き込む値。アップロード後の反映確認で目印として使いたい場合は
            テスト側から差し替えられるよう引数化している。

    Returns:
        (output_path, study_instance_uid, patient_id, study_description) のタプル。
        呼び出し元（テストコード）が「本当に今回生成したファイルの内容が
        画面に反映されたか」を照合するために使う。
    """
    now = datetime.datetime.now()
    date_str = now.strftime("%Y%m%d")
    time_str = now.strftime("%H%M%S")

    file_meta = FileMetaDataset()
    file_meta.MediaStorageSOPClassUID = SecondaryCaptureImageStorage
    file_meta.MediaStorageSOPInstanceUID = generate_uid()
    file_meta.TransferSyntaxUID = ExplicitVRLittleEndian
    file_meta.ImplementationClassUID = generate_uid()
    file_meta.ImplementationVersionName = "pydicom-testgen"

    ds = FileDataset(None, {}, file_meta=file_meta, preamble=b"\x00" * 128)
    ds.is_little_endian = True
    ds.is_implicit_VR = False

    ds.SpecificCharacterSet = "ISO_IR 192"
    ds.SOPClassUID = file_meta.MediaStorageSOPClassUID
    ds.SOPInstanceUID = file_meta.MediaStorageSOPInstanceUID
    ds.StudyDate = date_str
    ds.StudyTime = time_str
    ds.AccessionNumber = "ACC-PYTEST-0001"
    ds.Modality = modality
    ds.StudyDescription = study_description
    ds.SeriesDescription = "T1-PYTEST"
    ds.PatientName = patient_name
    ds.PatientID = patient_id
    ds.BodyPartExamined = "BRAIN"

    ds.StudyInstanceUID = generate_uid()
    ds.SeriesInstanceUID = generate_uid()
    ds.SeriesNumber = "1"
    ds.InstanceNumber = "1"

    rows, cols = 256, 256
    ds.SamplesPerPixel = 1
    ds.PhotometricInterpretation = "MONOCHROME2"
    ds.Rows = rows
    ds.Columns = cols
    ds.BitsAllocated = 8
    ds.BitsStored = 8
    ds.HighBit = 7
    ds.PixelRepresentation = 0

    # シンプルなグラデーション + 中央に円を描画したテストパターン
    y, x = np.mgrid[:rows, :cols]
    pixels = (x * 255 // cols).astype(np.uint8)
    circle_mask = (x - cols / 2) ** 2 + (y - rows / 2) ** 2 <= (rows / 4) ** 2
    pixels[circle_mask] = 255 - pixels[circle_mask]
    ds.PixelData = pixels.tobytes()

    if output_path is None:
        os.makedirs(DEFAULT_OUTPUT_DIR, exist_ok=True)
        output_path = os.path.join(DEFAULT_OUTPUT_DIR, f"pytest_sample_{ds.SOPInstanceUID}.dcm")

    ds.save_as(output_path, enforce_file_format=True)
    return output_path, str(ds.StudyInstanceUID), patient_id, study_description


if __name__ == "__main__":
    import sys

    # Windowsのコマンドプロンプト/PowerShellは既定のコードページがUTF-8でないことが多く、
    # 日本語の患者名等をそのままprintすると文字化けやエラーになることがあるための対策。
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    saved_path, study_instance_uid, saved_patient_id, saved_description = generate_sample_dicom()
    print("saved:", saved_path)
    print("StudyInstanceUID:", study_instance_uid)
    print("PatientID:", saved_patient_id)
    print("StudyDescription:", saved_description)
