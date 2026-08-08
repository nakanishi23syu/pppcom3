"""
test_upload.py
================
【シナリオ】ログイン → DICOMアップロード → 検査一覧への反映、を確認するE2Eテスト。

実際に動いているWorklist(Nuxt3)・Backend API・Temporal Worker等一式に対して、
Playwrightでブラウザを自動操作し、「ユーザーが実際にこの手順で使えるか」を確認する。
backend/DicomTool.Api.Tests（結合テスト、モックのTemporal Clientを使う）とは異なり、
ここでは本物のTemporal Workerが実際にファイルを取り込み、DBに登録され、
検査一覧に反映されるところまで含めて検証する（詳細な役割分担は tests/e2e/README.md 参照）。

【実行方法】
    pytest tests/e2e/test_upload.py -v -s

【前提条件】
    WORKLIST_BASE_URL で指定したWorklist・Backend API・Temporal Server・
    Temporal Workerがすべて起動していること（tests/e2e/README.md参照）。
"""

import os

from conftest import OUTPUT_DIR, WORKLIST_BASE_URL


def test_upload_dicom_file_appears_in_worklist(logged_in_page, sample_dicom_file):
    """
    ログイン済みのページからDICOMファイルを1つアップロードし、
    しばらく待ってから検査一覧（Worklistトップページ）に戻ったときに、
    アップロードした検査の内容（StudyDescription）が表示されていることを確認する。

    アップロード処理はTemporalワークフロー(UploadDicomWorkflow)経由の非同期処理
    （docs/CONTRACT.md 2章）のため、アップロード直後にはまだ検査一覧に反映されておらず、
    Workerがバックグラウンドで処理を終えるまで数秒の間が必要になる。
    そのため、このテストは実行環境の負荷によって稀に失敗する（Workerの処理が
    タイムアウト内に終わらない）ことがある、E2Eテストらしい"多少揺れのあるテスト"である。
    """
    page = logged_in_page

    page.goto(f"{WORKLIST_BASE_URL}/upload")
    page.wait_for_load_state("networkidle")
    page.screenshot(path=os.path.join(OUTPUT_DIR, "upload_1_upload_page.png"), full_page=True)

    page.set_input_files('input[type="file"]:not([webkitdirectory])', sample_dicom_file["path"])
    page.wait_for_timeout(500)
    page.screenshot(path=os.path.join(OUTPUT_DIR, "upload_2_file_selected.png"), full_page=True)

    page.get_by_role("button", name="アップロード", exact=True).click()
    # Temporalワークフローがバックグラウンドでファイルを取り込み終えるのを待つ
    # （固定時間待機。E2Eテストとしては簡易的だが、学習用としてわかりやすさを優先している）。
    page.wait_for_timeout(5000)
    page.screenshot(path=os.path.join(OUTPUT_DIR, "upload_3_after_upload.png"), full_page=True)

    upload_result_text = page.inner_text("body")
    assert "受け付けました" in upload_result_text or "アップロード" in upload_result_text, (
        f"アップロード後の画面に想定した文言が見つからない。画面のテキスト: {upload_result_text[:500]}"
    )

    # 検査一覧に戻って、今回アップロードした検査が反映されているか確認する。
    page.goto(f"{WORKLIST_BASE_URL}/")
    page.wait_for_load_state("networkidle")
    page.wait_for_timeout(2000)
    page.screenshot(path=os.path.join(OUTPUT_DIR, "upload_4_worklist_top.png"), full_page=True)

    worklist_text = page.inner_text("body")
    assert sample_dicom_file["study_description"] in worklist_text, (
        "アップロードした検査(StudyDescription="
        f"{sample_dicom_file['study_description']!r})が検査一覧に見つからない。"
        "Temporal Workerが起動しているか、処理が完了するまでの待機時間が"
        "十分か確認すること。画面のテキスト(先頭500文字): "
        f"{worklist_text[:500]}"
    )
