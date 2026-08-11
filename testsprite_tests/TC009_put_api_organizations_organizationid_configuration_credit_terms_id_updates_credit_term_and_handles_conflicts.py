import requests
import uuid

BASE_URL = "http://localhost:5155"
LOGIN_URL = f"{BASE_URL}/api/auth/login"
ORGANIZATION_ID = "857267cc-546a-49d0-91ba-3c2ae61d58c5"
CREDIT_TERMS_URL = f"{BASE_URL}/api/organizations/{ORGANIZATION_ID}/configuration/credit-terms"
TIMEOUT = 30

EMAIL = "phase2.tester@example.com"
PASSWORD = "Phase2Test!2026"

def login_and_get_session():
    session = requests.Session()
    resp = session.post(
        LOGIN_URL,
        json={"email": EMAIL, "password": PASSWORD},
        timeout=TIMEOUT
    )
    assert resp.status_code == 200, f"Login failed with status {resp.status_code}: {resp.text}"
    # erp_auth cookie will be in the session now
    return session

def create_credit_term(session, name, dueDays):
    resp = session.post(
        CREDIT_TERMS_URL,
        json={"name": name, "dueDays": dueDays},
        timeout=TIMEOUT
    )
    if resp.status_code == 201:
        return resp.json()
    elif resp.status_code == 409:
        return None  # duplicate name exists
    else:
        resp.raise_for_status()

def update_credit_term(session, credit_term_id, name, dueDays, isActive):
    url = f"{CREDIT_TERMS_URL}/{credit_term_id}"
    resp = session.put(
        url,
        json={"name": name, "dueDays": dueDays, "isActive": isActive},
        timeout=TIMEOUT
    )
    return resp

def delete_credit_term(session, credit_term_id):
    url = f"{CREDIT_TERMS_URL}/{credit_term_id}"
    resp = session.delete(url, timeout=TIMEOUT)
    return resp

def get_another_existing_credit_term_id(session, exclude_id):
    resp = session.get(CREDIT_TERMS_URL, timeout=TIMEOUT)
    assert resp.status_code == 200, f"Failed to list credit terms: {resp.text}"
    items = resp.json()
    for item in items:
        if item["id"] != exclude_id:
            return item["id"], item["name"]
    return None, None

def test_put_api_organizations_organizationid_configuration_credit_terms_id_updates_credit_term_and_handles_conflicts():
    session = login_and_get_session()

    # Create 2 credit terms for testing update and conflict
    unique_name1 = f"TestTerm-{uuid.uuid4().hex[:8]}"
    unique_name2 = f"TestTerm-{uuid.uuid4().hex[:8]}"
    created1 = create_credit_term(session, unique_name1, 10)
    assert created1 is not None, "Failed to create first credit term for testing"

    created2 = create_credit_term(session, unique_name2, 20)
    assert created2 is not None, "Failed to create second credit term for testing"

    try:
        credit_term_id = created1["id"]

        # Test successful update: change name, dueDays, and isActive
        new_name = unique_name1 + "-Updated"
        new_dueDays = 15
        new_isActive = False
        resp = update_credit_term(session, credit_term_id, new_name, new_dueDays, new_isActive)
        assert resp.status_code == 200, f"Expected 200 on update, got {resp.status_code}: {resp.text}"
        updated_data = resp.json()
        assert updated_data["id"] == credit_term_id
        assert updated_data["name"] == new_name
        assert updated_data["dueDays"] == new_dueDays
        assert updated_data["isActive"] == new_isActive

        # Test 404 not found - use a random UUID that likely does not exist
        non_existent_id = str(uuid.uuid4())
        resp_404 = update_credit_term(session, non_existent_id, "AnyName", 10, True)
        assert resp_404.status_code == 404, f"Expected 404 for non-existent id, got {resp_404.status_code}: {resp_404.text}"

        # Test 409 conflict by trying to rename credit_term_id to have the name of created2
        resp_409 = update_credit_term(session, credit_term_id, unique_name2, 30, True)
        assert resp_409.status_code == 409, f"Expected 409 conflict on duplicate name, got {resp_409.status_code}: {resp_409.text}"

    finally:
        # Cleanup created credit terms
        for term in [created1, created2]:
            if term:
                del_resp = delete_credit_term(session, term["id"])
                # 204 expected or 404 if already deleted
                assert del_resp.status_code in (204, 404), f"Cleanup delete failed with status {del_resp.status_code}: {del_resp.text}"

test_put_api_organizations_organizationid_configuration_credit_terms_id_updates_credit_term_and_handles_conflicts()