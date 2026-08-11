import requests

BASE_URL = "http://localhost:5155"
ORG_ID = "857267cc-546a-49d0-91ba-3c2ae61d58c5"
ADMIN_EMAIL = "phase2.tester@example.com"
ADMIN_PASSWORD = "Phase2Test!2026"
TIMEOUT = 30

def test_post_api_organizations_organizationid_invitations_invites_user_with_permission():
    session = requests.Session()
    login_url = f"{BASE_URL}/api/auth/login"
    invitations_url = f"{BASE_URL}/api/organizations/{ORG_ID}/invitations"

    # Login as the known Admin user to get authenticated session
    login_payload = {
        "email": ADMIN_EMAIL,
        "password": ADMIN_PASSWORD
    }
    login_resp = session.post(login_url, json=login_payload, timeout=TIMEOUT, verify=False)
    assert login_resp.status_code == 200, f"Admin login failed: {login_resp.text}"

    # Use a test invitee email (must not be the admin email)
    test_email = "invitee.user@example.com"
    test_role = "Member"  # or "Admin" but Member is safer for invitation tests

    # Prepare invitation payload
    invite_payload = {
        "email": test_email,
        "role": test_role
    }

    # POST invitation as Admin user - expecting 200 success with membershipId, email, role
    invite_resp = session.post(invitations_url, json=invite_payload, timeout=TIMEOUT, verify=False)
    try:
        assert invite_resp.status_code == 200, f"Expected 200 OK on invite, got {invite_resp.status_code}: {invite_resp.text}"
        data = invite_resp.json()
        assert "membershipId" in data and isinstance(data["membershipId"], str) and data["membershipId"], "membershipId missing or invalid"
        assert data.get("email") == test_email, "Returned email mismatch"
        assert data.get("role") == test_role, "Returned role mismatch"
    finally:
        # Cleanup: delete the invited membership if possible - Not part of the given API, so no deletion here.
        # The test plan does not mention deletion endpoint for invitations or memberships.
        pass

    # Since no Member credentials provided, skip testing 403 for caller lacking InviteUser permission per instructions.

test_post_api_organizations_organizationid_invitations_invites_user_with_permission()