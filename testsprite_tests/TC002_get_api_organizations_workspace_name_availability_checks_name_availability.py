import requests

BASE_URL = "http://localhost:5155"
LOGIN_URL = f"{BASE_URL}/api/auth/login"
WORKSPACE_NAME_AVAILABILITY_URL = f"{BASE_URL}/api/organizations/workspace-name-availability"
EMAIL = "phase2.tester@example.com"
PASSWORD = "Phase2Test!2026"
TIMEOUT = 30


def test_get_api_organizations_workspace_name_availability_checks_name_availability():
    session = requests.Session()
    try:
        # Authenticate with the known Admin user
        login_resp = session.post(
            LOGIN_URL,
            json={"email": EMAIL, "password": PASSWORD},
            timeout=TIMEOUT,
            verify=False
        )
        assert login_resp.status_code == 200, f"Login failed: {login_resp.text}"
        login_data = login_resp.json()
        assert "userId" in login_data and "email" in login_data and "fullName" in login_data

        # Use a valid workspace name (known existing org has workspaceName "phase2-test-org")
        # Test with a workspace name that likely is taken for isAvailable = False
        params_taken = {"workspaceName": "phase2-test-org"}
        resp_taken = session.get(
            WORKSPACE_NAME_AVAILABILITY_URL,
            params=params_taken,
            timeout=TIMEOUT,
            verify=False
        )
        assert resp_taken.status_code == 200, f"Failed request on taken workspaceName: {resp_taken.text}"
        data_taken = resp_taken.json()
        assert "isAvailable" in data_taken
        # The workspaceName "phase2-test-org" should not be available
        assert data_taken["isAvailable"] is False or data_taken["isAvailable"] == False

        # Test with a workspace name that likely is available for isAvailable = True
        params_available = {"workspaceName": "this-workspace-name-should-be-available-xyz123"}
        resp_available = session.get(
            WORKSPACE_NAME_AVAILABILITY_URL,
            params=params_available,
            timeout=TIMEOUT,
            verify=False
        )
        assert resp_available.status_code == 200, f"Failed request on available workspaceName: {resp_available.text}"
        data_available = resp_available.json()
        assert "isAvailable" in data_available
        assert isinstance(data_available["isAvailable"], bool)

    finally:
        session.close()


test_get_api_organizations_workspace_name_availability_checks_name_availability()
