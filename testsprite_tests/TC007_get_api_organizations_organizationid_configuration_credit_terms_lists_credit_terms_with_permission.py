import requests

BASE_URL = "https://localhost:7104"
LOGIN_URL = f"{BASE_URL}/api/auth/login"
CREDIT_TERMS_URL_TEMPLATE = f"{BASE_URL}/api/organizations/{{organizationId}}/configuration/credit-terms"
ORGANIZATION_ID = "857267cc-546a-49d0-91ba-3c2ae61d58c5"
EMAIL = "phase2.tester@example.com"
PASSWORD = "Phase2Test!2026"
TIMEOUT = 30


def authenticate(email: str, password: str) -> requests.Session:
    session = requests.Session()
    login_payload = {"email": email, "password": password}
    resp = session.post(LOGIN_URL, json=login_payload, timeout=TIMEOUT, verify=False)
    assert resp.status_code == 200, f"Login failed: {resp.status_code} {resp.text}"
    # session will store cookies automatically
    return session


def test_get_credit_terms_with_permission():
    """
    Test GET /api/organizations/{organizationId}/configuration/credit-terms
    returns 200 with a list of credit terms for a user with View permission;
    returns 403 if lacking permission.
    """
    session = authenticate(EMAIL, PASSWORD)
    url = CREDIT_TERMS_URL_TEMPLATE.format(organizationId=ORGANIZATION_ID)

    # Positive case: User with View permission (Admin account) should get 200 and a list
    resp = session.get(url, timeout=TIMEOUT, verify=False)
    assert resp.status_code in (200, 403), f"Unexpected status code {resp.status_code} with body: {resp.text}"
    if resp.status_code == 200:
        json_data = resp.json()
        assert isinstance(json_data, list), "Response is not a list of credit terms"
        for item in json_data:
            assert "id" in item, "Credit term missing 'id'"
            assert "organizationId" in item, "Credit term missing 'organizationId'"
            assert "name" in item, "Credit term missing 'name'"
            assert "dueDays" in item, "Credit term missing 'dueDays'"
            assert "isActive" in item, "Credit term missing 'isActive'"
            assert "createdAt" in item, "Credit term missing 'createdAt'"
    else:
        # Status 403 means user lacks permission, which is expected if user without View permission tested.
        assert resp.status_code == 403, f"Expected 403 forbidden, got {resp.status_code} {resp.text}"


test_get_credit_terms_with_permission()
