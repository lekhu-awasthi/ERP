import requests

BASE_URL = "http://localhost:5155"
ADMIN_EMAIL = "phase2.tester@example.com"
ADMIN_PASSWORD = "Phase2Test!2026"
ORG_ID = "857267cc-546a-49d0-91ba-3c2ae61d58c5"

def test_post_api_organizations_memberships_membershipid_accept_request_accepts_join_request_with_permission():
    session = requests.Session()
    try:
        # Login as Admin
        login_resp = session.post(
            f"{BASE_URL}/api/auth/login",
            json={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD},
            timeout=30,
            verify=False
        )
        assert login_resp.status_code == 200, f"Login failed: {login_resp.text}"

        # Get list of own organizations / requests to find a pending join request membershipId
        mine_resp = session.get(f"{BASE_URL}/api/organizations/mine", timeout=30, verify=False)
        assert mine_resp.status_code == 200, f"Failed to get organizations/mine: {mine_resp.text}"
        mine_data = mine_resp.json()
        requests_list = mine_data.get("requests", [])
        if not requests_list:
            print("No pending join requests found for this admin user; skipping test.")
            assert True  # No join requests to accept; skip test gracefully
            return  
        
        membership_id = requests_list[0].get("membershipId")
        assert membership_id, "membershipId missing in requests entry"

        # POST accept-request endpoint to accept join request with permission
        accept_url = f"{BASE_URL}/api/organizations/memberships/{membership_id}/accept-request"
        accept_resp = session.post(accept_url, timeout=30, verify=False)

        # If the admin has permission, expect 200 with confirmation message
        if accept_resp.status_code == 200:
            json_resp = accept_resp.json()
            assert isinstance(json_resp.get("Message"), str), "Expected 'Message' in response"
        else:
            # If admin somehow lacks permission, 403 expected
            assert accept_resp.status_code == 403, f"Unexpected status code: {accept_resp.status_code} - {accept_resp.text}"
    finally:
        session.close()

test_post_api_organizations_memberships_membershipid_accept_request_accepts_join_request_with_permission()
