"""
_common.py
===========
tools/配下の各スクリプトが共通で使うDICOM操作ヘルパー。単体で実行するものではなく、
同じフォルダにある他のスクリプト（dicom_tag_editor.py等）から import して使う。
"""

import copy
import uuid
from pathlib import Path

import pydicom
from pydicom.datadict import tag_for_keyword
from pydicom.uid import generate_uid


def find_dicom_files(input_dir):
    """input_dir直下（再帰しない）にある.dcmファイルの一覧をPathのリストで返す。"""
    input_dir = Path(input_dir)
    return sorted(p for p in input_dir.glob("*.dcm") if p.is_file())


def read_dataset(path):
    """
    force=Trueで読み込む。会社の元画像や自作の簡易ファイルには、正式な
    128バイトpreamble+'DICM'ヘッダが付いていないものが混ざっていることがあるため。
    """
    return pydicom.dcmread(str(path), force=True)


def save_dataset(ds, output_dir, filename=None):
    """output_dirが無ければ作成した上でdsを保存し、保存先のPathを返す。"""
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    if filename is None:
        filename = f"{ds.SOPInstanceUID}.dcm"
    output_path = output_dir / filename
    ds.save_as(str(output_path), enforce_file_format=True)
    return output_path


def generate_random_id(prefix="", length=8):
    """
    uuid4を使って、ランダムな英数字ID（PatientID等に使いやすい短い文字列）を生成する。
    連番ではなく「毎回重複しないID」が欲しい時に使う。

    例: generate_random_id("PT") -> "PT3F9A21B4"
    """
    return f"{prefix}{uuid.uuid4().hex[:length].upper()}"


def apply_tag_overrides(ds, overrides):
    """
    overrides: {"PatientName": "テスト 太郎", "Modality": "CT"} のようなdict。
    存在しないキーワードを指定した場合は、気づかず無視されるのを防ぐためKeyErrorで落とす。
    """
    for keyword, value in overrides.items():
        if tag_for_keyword(keyword) is None:
            raise KeyError(
                f"'{keyword}' はDICOMの標準タグキーワードではありません（スペルを確認してください）"
            )
        setattr(ds, keyword, value)


def regenerate_instance_uids(ds, *, new_study=False, new_series=False):
    """
    SOPInstanceUIDは常に新規発行する。new_study/new_seriesがTrueならそれぞれも新規発行する。
    file_meta.MediaStorageSOPInstanceUIDもSOPInstanceUIDと必ず一致させる
    （ずれているとfo-dicom側の整合性チェックで弾かれるため）。
    """
    ds.SOPInstanceUID = generate_uid()
    if hasattr(ds, "file_meta"):
        ds.file_meta.MediaStorageSOPInstanceUID = ds.SOPInstanceUID
    if new_series:
        ds.SeriesInstanceUID = generate_uid()
    if new_study:
        ds.StudyInstanceUID = generate_uid()


def build_study(
    template_ds,
    *,
    series_count,
    instances_per_series,
    patient_id=None,
    patient_name=None,
    study_description=None,
    modalities=None,
    study_instance_uid=None,
):
    """
    テンプレートデータセットを元に、1つのStudy配下にseries_count個のSeries、
    各Seriesにinstances_per_series個のSOPを持つデータセット群をメモリ上に生成する
    （保存はしない。呼び出し側でsave_dataset()を使って書き出すこと）。

    modalities を渡すと、Seriesごとに順番に（余ったら先頭に戻って）割り当てる。

    Returns: list[pydicom.Dataset]
    """
    study_uid = study_instance_uid or generate_uid()
    results = []
    for series_idx in range(series_count):
        series_uid = generate_uid()
        modality = modalities[series_idx % len(modalities)] if modalities else None
        for instance_idx in range(instances_per_series):
            ds = copy.deepcopy(template_ds)
            ds.StudyInstanceUID = study_uid
            ds.SeriesInstanceUID = series_uid
            ds.SeriesNumber = str(series_idx + 1)
            ds.InstanceNumber = str(instance_idx + 1)
            ds.SOPInstanceUID = generate_uid()
            if hasattr(ds, "file_meta"):
                ds.file_meta.MediaStorageSOPInstanceUID = ds.SOPInstanceUID
            if patient_id is not None:
                ds.PatientID = patient_id
            if patient_name is not None:
                ds.PatientName = patient_name
            if study_description is not None:
                ds.StudyDescription = study_description
            if modality is not None:
                ds.Modality = modality
            results.append(ds)
    return results
