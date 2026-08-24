"""
dicom_seg_sample_generator.py
================================
CT様の疑似断面画像シリーズ（ソース画像）と、そのシリーズを正しく参照する
Segmentation Storage（SEGモダリティ、SOP Class UID 1.2.840.10008.5.1.4.1.1.66.4）を
highdicomでゼロから生成するツール。

SEGは他モダリティと違い、ReferencedSeriesSequence/SourceImageSequence/
FrameOfReferenceUID等でソース画像側とタグの整合性が取れていないと、多くのビューワや
バリデータに弾かれる。既存の`input/sample1.dcm`等（Secondary Capture、位置情報タグなし）
はSEGの元画像として使えないため、このスクリプトでは幾何情報
（PixelSpacing/ImagePositionPatient/ImageOrientationPatient等）を持つCT断面画像
シリーズを自前で組み立て、それを参照する形でSEGを生成する
（ReferencedSeriesSequence等の整合性はhighdicomが自動で担保してくれる）。

【使い方】
下にある main() 内の「ここを自由に書き換えてください」ブロックだけを目的に合わせて
書き換えて、このファイルをそのまま実行する。
    python tools/dicom_test_data_generator/dicom_seg_sample_generator.py

実際の処理（ソース画像の組み立て・SEGの構築・保存）は run_seg_generator() と
_make_ct_slice_pixels() に切り出してあるので、パラメータブロック以外は
よほど生成内容自体を変えたい時以外は触らなくてよい作りにしてある。

【必要パッケージ】
pydicomに加えてhighdicomが必要（requirements.txtに追加済み）。
    pip install -r tools/dicom_test_data_generator/requirements.txt
"""

import sys

import numpy as np
import pydicom
from pydicom.dataset import FileMetaDataset
from pydicom.sr.codedict import codes
from pydicom.sr.coding import Code
from pydicom.uid import CTImageStorage, ExplicitVRLittleEndian, generate_uid

from highdicom.seg import Segmentation, SegmentDescription
from highdicom.seg.enum import SegmentAlgorithmTypeValues, SegmentationTypeValues
from highdicom.content import AlgorithmIdentificationSequence

from _common import save_dataset


def main():
    # ==================== ここを自由に書き換えてください ====================
    output_dir = "tools/dicom_test_data_generator/output/seg_sample"

    rows = 128
    columns = 128
    num_slices = 20  # ソースシリーズの断面数
    pixel_spacing_mm = 1.0  # mm（Rows/Columns方向とも同じ値を使う）
    slice_spacing_mm = 2.0  # mm（断面間隔＝SliceThickness）

    patient_id = "SEGP001"
    patient_name = "テスト^セグメント太郎"  # PN VRの規約に合わせて「姓^名」の形にする
    study_description = "SEGサンプル生成用CTスタディ"

    segment_label = "Liver"
    # SNOMED CT: 123037004 = Anatomical Structure, 10200004 = Liver
    segmented_property_category = Code("123037004", "SCT", "Anatomical Structure")
    segmented_property_type = Code("10200004", "SCT", "Liver")
    # ======================================================================

    run_seg_generator(
        output_dir,
        rows,
        columns,
        num_slices,
        pixel_spacing_mm,
        slice_spacing_mm,
        patient_id,
        patient_name,
        study_description,
        segment_label,
        segmented_property_category,
        segmented_property_type,
    )


def _make_ct_slice_pixels(rows, columns, slice_ratio):
    """
    体輪郭（円・軟部組織相当）とその内部の肝臓様の楕円ROIを持つ、疑似的なCT断面の
    HU値配列（int16）を1枚分作る。

    slice_ratio: 0.0(スタック上端)〜1.0(下端)。中央付近で肝臓ROIが最大になり、
    上下端に向かって小さくなる（0になったら消える）ようにして、実際の臓器のように
    一部の断面にしか写らない状態を再現する。

    Returns: (pixels, liver_mask) のタプル（どちらもshape=(rows, columns)）
    """
    yy, xx = np.mgrid[0:rows, 0:columns]
    cy, cx = rows / 2, columns / 2

    pixels = np.full((rows, columns), -1000, dtype=np.int16)  # 空気

    body_radius = min(rows, columns) * 0.42
    body_mask = ((yy - cy) ** 2 + (xx - cx) ** 2) <= body_radius**2
    pixels[body_mask] = 40  # 軟部組織相当

    # 中央(slice_ratio=0.5)で最大、上下端(0.0/1.0)に向けて0まで小さくなる係数
    taper = max(0.0, 1.0 - abs(slice_ratio - 0.5) / 0.35)

    liver_mask = np.zeros((rows, columns), dtype=bool)
    if taper > 0.0:
        liver_cy, liver_cx = cy + 12, cx + 18
        liver_ry = rows * 0.16 * taper
        liver_rx = columns * 0.20 * taper
        liver_mask = (
            ((yy - liver_cy) / liver_ry) ** 2 + ((xx - liver_cx) / liver_rx) ** 2
        ) <= 1.0
        liver_mask &= body_mask
        pixels[liver_mask] = 70  # 肝臓相当

    return pixels, liver_mask


def _build_source_series(
    rows,
    columns,
    num_slices,
    pixel_spacing_mm,
    slice_spacing_mm,
    patient_id,
    patient_name,
    study_description,
):
    """
    幾何情報（PixelSpacing/ImagePositionPatient/ImageOrientationPatient等）を
    正しく持つ、疑似CT断面画像シリーズ（pydicom Datasetのリスト）を1つ組み立てる。
    このシリーズがSEGのソース画像（ReferencedSeriesSequence等の参照先）になる。

    Returns: (source_datasets, liver_mask_stack)
        source_datasets: list[pydicom.Dataset]（num_slices件、Z順）
        liver_mask_stack: shape=(num_slices, rows, columns)のbool ndarray
    """
    study_instance_uid = generate_uid()
    series_instance_uid = generate_uid()
    frame_of_reference_uid = generate_uid()

    study_date = "20260101"
    study_time = "090000"

    source_datasets = []
    liver_masks = []

    for i in range(num_slices):
        slice_ratio = i / (num_slices - 1) if num_slices > 1 else 0.5
        pixels, liver_mask = _make_ct_slice_pixels(rows, columns, slice_ratio)
        liver_masks.append(liver_mask)

        file_meta = FileMetaDataset()
        sop_instance_uid = generate_uid()
        file_meta.MediaStorageSOPClassUID = CTImageStorage
        file_meta.MediaStorageSOPInstanceUID = sop_instance_uid
        file_meta.TransferSyntaxUID = ExplicitVRLittleEndian

        ds = pydicom.Dataset()
        ds.file_meta = file_meta

        ds.SOPClassUID = CTImageStorage
        ds.SOPInstanceUID = sop_instance_uid
        ds.Modality = "CT"
        ds.ImageType = ["ORIGINAL", "PRIMARY", "AXIAL"]

        # PatientName等に日本語を入れるため、値を代入する前に文字コードを設定しておく
        # （設定前に代入するとASCII扱いになり文字化けする）。
        ds.SpecificCharacterSet = "ISO_IR 192"  # UTF-8
        ds.PatientID = patient_id
        ds.PatientName = patient_name
        ds.PatientBirthDate = ""
        ds.PatientSex = ""

        ds.StudyInstanceUID = study_instance_uid
        ds.StudyDate = study_date
        ds.StudyTime = study_time
        ds.StudyID = "1"
        ds.AccessionNumber = "SEGSAMPLE001"
        ds.ReferringPhysicianName = ""
        ds.StudyDescription = study_description

        ds.SeriesInstanceUID = series_instance_uid
        ds.SeriesNumber = 1
        ds.SeriesDescription = "Synthetic CT (SEG sample source)"
        ds.InstanceNumber = i + 1

        ds.FrameOfReferenceUID = frame_of_reference_uid
        ds.PositionReferenceIndicator = ""

        ds.Rows = rows
        ds.Columns = columns
        ds.PixelSpacing = [pixel_spacing_mm, pixel_spacing_mm]
        ds.SliceThickness = slice_spacing_mm
        ds.SpacingBetweenSlices = slice_spacing_mm
        z = i * slice_spacing_mm
        ds.ImagePositionPatient = [
            -columns * pixel_spacing_mm / 2,
            -rows * pixel_spacing_mm / 2,
            z,
        ]
        ds.ImageOrientationPatient = [1, 0, 0, 0, 1, 0]
        ds.SliceLocation = z

        ds.SamplesPerPixel = 1
        ds.PhotometricInterpretation = "MONOCHROME2"
        ds.BitsAllocated = 16
        ds.BitsStored = 16
        ds.HighBit = 15
        ds.PixelRepresentation = 1
        ds.RescaleIntercept = 0
        ds.RescaleSlope = 1
        ds.RescaleType = "HU"
        ds.KVP = ""

        ds.PixelData = pixels.tobytes()

        source_datasets.append(ds)

    return source_datasets, np.stack(liver_masks, axis=0)


def run_seg_generator(
    output_dir,
    rows,
    columns,
    num_slices,
    pixel_spacing_mm,
    slice_spacing_mm,
    patient_id,
    patient_name,
    study_description,
    segment_label,
    segmented_property_category,
    segmented_property_type,
):
    """疑似CTソースシリーズを組み立てて保存し、それを参照するSEGを1件生成して保存する。"""
    source_datasets, liver_mask_stack = _build_source_series(
        rows,
        columns,
        num_slices,
        pixel_spacing_mm,
        slice_spacing_mm,
        patient_id,
        patient_name,
        study_description,
    )

    source_output_dir = f"{output_dir}/source_ct"
    for ds in source_datasets:
        save_dataset(ds, source_output_dir)

    segment_description = SegmentDescription(
        segment_number=1,
        segment_label=segment_label,
        segmented_property_category=segmented_property_category,
        segmented_property_type=segmented_property_type,
        algorithm_type=SegmentAlgorithmTypeValues.AUTOMATIC,
        algorithm_identification=AlgorithmIdentificationSequence(
            name="dicom-tool-3-sample-seg-generator",
            version="1.0",
            # DCM 123104 = Morphological Operations（円・楕円の形状演算で作った疑似ROIのため）
            family=codes.DCM.MorphologicalOperations,
        ),
    )

    seg = Segmentation(
        source_images=source_datasets,
        pixel_array=liver_mask_stack,
        segmentation_type=SegmentationTypeValues.BINARY,
        segment_descriptions=[segment_description],
        series_instance_uid=generate_uid(),
        series_number=100,
        sop_instance_uid=generate_uid(),
        instance_number=1,
        manufacturer="dicom-tool-3 project",
        manufacturer_model_name="dicom_seg_sample_generator",
        software_versions="1.0",
        device_serial_number="N/A",
        content_description=f"{segment_label} segmentation (synthetic sample)",
        content_creator_name="dicom-tool-3^SampleGenerator",
        series_description=f"{segment_label} SEG (synthetic sample)",
    )

    seg_path = save_dataset(seg, output_dir, filename="segmentation.dcm")

    print(
        f"完了: 疑似CTソース画像 {len(source_datasets)}件を {source_output_dir} に、"
        f"SEG(Segmentation Storage) 1件を {seg_path} に出力しました。"
    )


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    main()
