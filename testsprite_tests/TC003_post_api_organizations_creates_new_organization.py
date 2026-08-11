import requests
import uuid

BASE_URL = "http://localhost:5155"
LOGIN_URL = f"{BASE_URL}/api/auth/login"
ORGANIZATIONS_URL = f"{BASE_URL}/api/organizations"
TIMEOUT = 30

# Admin user credentials from instructions
ADMIN_EMAIL = "phase2.tester@example.com"
ADMIN_PASSWORD = "Phase2Test!2026"


def test_post_api_organizations_creates_new_organization():
    session = requests.Session()
    try:
        # Authenticate and obtain session cookie
        login_resp = session.post(
            LOGIN_URL,
            json={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD},
            timeout=TIMEOUT,
            verify=False
        )
        assert login_resp.status_code == 200, f"Login failed: {login_resp.text}"
        # Session cookie (erp_auth) is automatically handled by requests.Session

        # Prepare unique workspaceName for uniqueness
        unique_workspace_name = f"test-org-{uuid.uuid4().hex[:8]}"

        new_org_data = {
            "name": "Test Organization",
            "industry": "Technology",
            "address": "123 Test St, Test City",
            "accountingStartDate": "2024-01-01",
            "isVatRegistered": True,
            "workspaceName": unique_workspace_name,
            "email": "contact@testorg.com",
            "phone": "+1234567890",
            "panNumber": "ABCDE1234F",
            "website": "https://www.testorg.com",
            "trackInventory": True,
            "multipleLocations": False,
            "multipleWarehouses": False,
            "multiCurrency": True,
            "manufacturing": False,
            "posRetail": False,
            "posRestaurant": False,
        }

        # Call POST /api/organizations to create organization
        create_resp = session.post(
            ORGANIZATIONS_URL, json=new_org_data, timeout=TIMEOUT, verify=False
        )

        assert create_resp.status_code == 201, f"Expected 201 Created, got {create_resp.status_code}: {create_resp.text}"
        resp_json = create_resp.json()
        # Validate response contains organizationId, name, workspaceName with expected types
        assert "organizationId" in resp_json and isinstance(resp_json["organizationId"], str) and resp_json["organizationId"], "organizationId missing or invalid"
        assert resp_json.get("name") == new_org_data["name"], f"Name in response {resp_json.get('name')} does not match request {new_org_data['name']}"
        assert resp_json.get("workspaceName") == unique_workspace_name, f"workspaceName in response {resp_json.get('workspaceName')} does not match request {unique_workspace_name}"

    finally:
        # Cleanup: Delete the created organization to avoid test pollution
        # Delete endpoint: DELETE /api/organizations/{organizationId} requires Manage permission, assuming Admin user has it
        if 'resp_json' in locals() and "organizationId" in resp_json:
            org_id = resp_json["organizationId"]
            delete_url = f"{ORGANIZATIONS_URL}/{org_id}"
            try:
                del_resp = session.delete(delete_url, timeout=TIMEOUT, verify=False)
                # Allowed: 204 No Content on success, 404 if not found (ignore for cleanup)
                if del_resp.status_code not in (204, 404):
                    print(f"Warning: Failed to delete organization {org_id}. Status {del_resp.status_code}: {del_resp.text}")
            except Exception as e:
                print(f"Exception during cleanup delete organization {org_id}: {e}")


test_post_api_organizations_creates_new_organization()
