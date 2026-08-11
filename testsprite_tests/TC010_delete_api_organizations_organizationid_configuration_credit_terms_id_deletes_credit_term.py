import requests

BASE_URL = "http://localhost:5155"
ORG_ID = "857267cc-546a-49d0-91ba-3c2ae61d58c5"
LOGIN_EMAIL = "phase2.tester@example.com"
LOGIN_PASSWORD = "Phase2Test!2026"
TIMEOUT = 30


def test_delete_credit_term_deletes_and_returns_correct_status():
    session = requests.Session()

    # Login to get session cookie
    login_resp = session.post(
        f"{BASE_URL}/api/auth/login",
        json={"email": LOGIN_EMAIL, "password": LOGIN_PASSWORD},
        timeout=TIMEOUT,
    )
    assert login_resp.status_code == 200, f"Login failed: {login_resp.text}"
    assert "erp_auth" in session.cookies, "erp_auth cookie not set after login"

    created_id = None
    try:
        # Create a credit term to delete
        create_resp = session.post(
            f"{BASE_URL}/api/organizations/{ORG_ID}/configuration/credit-terms",
            json={"name": "Test Term Delete", "dueDays": 15},
            timeout=TIMEOUT,
        )
        assert create_resp.status_code == 201, f"Create credit term failed: {create_resp.text}"
        created = create_resp.json()
        created_id = created.get("id")
        assert created_id, "Created credit term ID missing"

        # Delete the created credit term - expect 204
        delete_resp = session.delete(
            f"{BASE_URL}/api/organizations/{ORG_ID}/configuration/credit-terms/{created_id}",
            timeout=TIMEOUT,
        )
        assert delete_resp.status_code == 204, f"Expected 204 on delete, got {delete_resp.status_code} with body: {delete_resp.text}"

        # Delete again (resource no longer exists) - expect 404
        delete_resp_2 = session.delete(
            f"{BASE_URL}/api/organizations/{ORG_ID}/configuration/credit-terms/{created_id}",
            timeout=TIMEOUT,
        )
        assert delete_resp_2.status_code == 404, f"Expected 404 on deleting non-existent credit term, got {delete_resp_2.status_code} with body: {delete_resp_2.text}"

    finally:
        # Cleanup: If credit term still exists (in case delete failed), attempt to delete to not leave test data
        if created_id:
            session.delete(
                f"{BASE_URL}/api/organizations/{ORG_ID}/configuration/credit-terms/{created_id}",
                timeout=TIMEOUT,
            )


test_delete_credit_term_deletes_and_returns_correct_status()