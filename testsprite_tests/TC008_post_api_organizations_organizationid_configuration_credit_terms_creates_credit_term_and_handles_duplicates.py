import requests
import uuid

BASE_URL = "http://localhost:5155"
ORG_ID = "857267cc-546a-49d0-91ba-3c2ae61d58c5"
LOGIN_EMAIL = "phase2.tester@example.com"
LOGIN_PASSWORD = "Phase2Test!2026"
TIMEOUT = 30

def test_post_api_organizations_organizationid_configuration_credit_terms_creates_credit_term_and_handles_duplicates():
    session = requests.Session()

    # Login to get authenticated session with cookie
    login_resp = session.post(
        f"{BASE_URL}/api/auth/login",
        json={"email": LOGIN_EMAIL, "password": LOGIN_PASSWORD},
        timeout=TIMEOUT,
        verify=False
    )
    assert login_resp.status_code == 200, f"Login failed: {login_resp.text}"

    headers = {"Content-Type": "application/json"}

    # Generate a unique credit term name to avoid collisions
    unique_name = f"Net-{uuid.uuid4().hex[:8]}"
    url = f"{BASE_URL}/api/organizations/{ORG_ID}/configuration/credit-terms"

    # Step 1: Create a new credit term - expect 201
    create_data = {"name": unique_name, "dueDays": 30}
    create_resp = session.post(url, json=create_data, headers=headers, timeout=TIMEOUT, verify=False)
    assert create_resp.status_code == 201, f"Expected 201 on create, got {create_resp.status_code}: {create_resp.text}"
    created_term = create_resp.json()
    assert created_term["name"] == unique_name
    assert created_term["dueDays"] == 30
    term_id = created_term.get("id")
    assert term_id, "Response missing credit term id"

    try:
        # Step 2: Attempt to create duplicate credit term (same name) - expect 409 Conflict
        duplicate_resp = session.post(url, json=create_data, headers=headers, timeout=TIMEOUT, verify=False)
        assert duplicate_resp.status_code == 409, f"Expected 409 on duplicate create, got {duplicate_resp.status_code}: {duplicate_resp.text}"

        # Step 3: Skipped due to no Member creds

    finally:
        # Cleanup: delete created credit term
        delete_resp = session.delete(f"{url}/{term_id}", headers=headers, timeout=TIMEOUT, verify=False)
        assert delete_resp.status_code == 204, f"Cleanup delete failed: {delete_resp.status_code} {delete_resp.text}"


test_post_api_organizations_organizationid_configuration_credit_terms_creates_credit_term_and_handles_duplicates()
