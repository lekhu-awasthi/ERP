import requests

BASE_URL = "https://localhost:7104"
ADMIN_EMAIL = "phase2.tester@example.com"
ADMIN_PASSWORD = "Phase2Test!2026"
ORG_ID = "857267cc-546a-49d0-91ba-3c2ae61d58c5"
INVITED_EMAIL = "phase2.invited@example.com"  # This should be an invited user email controlled for test
INVITED_PASSWORD = "Phase2InvitedUser!2026"  # Known password for the invited user

def test_post_api_organizations_memberships_membershipid_accept_invitation_accepts_invitation():
    session_admin = requests.Session()
    session_invited = requests.Session()

    try:
        # Login as Admin to send an invitation
        login_response = session_admin.post(
            f"{BASE_URL}/api/auth/login",
            json={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD},
            timeout=30,
            verify=False
        )
        assert login_response.status_code == 200, "Admin login failed"
        # Ensure cookie is set
        assert "erp_auth" in session_admin.cookies, "erp_auth cookie missing in admin session"

        # Send invitation to invited user
        invitation_payload = {"email": INVITED_EMAIL, "role": "Member"}
        invite_resp = session_admin.post(
            f"{BASE_URL}/api/organizations/{ORG_ID}/invitations",
            json=invitation_payload,
            timeout=30,
            verify=False
        )
        assert invite_resp.status_code == 200, f"Invitation failed: {invite_resp.status_code} {invite_resp.text}"
        invite_data = invite_resp.json()
        membership_id = invite_data.get("membershipId")
        assert membership_id, "membershipId not returned in invitation response"
        assert invite_data.get("email") == INVITED_EMAIL, "Invited email mismatch in response"
        assert invite_data.get("role") == "Member", "Role mismatch in invitation response"

        # Login as invited user to accept invitation
        login_invited_resp = session_invited.post(
            f"{BASE_URL}/api/auth/login",
            json={"email": INVITED_EMAIL, "password": INVITED_PASSWORD},
            timeout=30,
            verify=False
        )
        assert login_invited_resp.status_code == 200, "Invited user login failed"
        assert "erp_auth" in session_invited.cookies, "erp_auth cookie missing in invited user session"

        # POST accept-invitation as invited user
        accept_resp = session_invited.post(
            f"{BASE_URL}/api/organizations/memberships/{membership_id}/accept-invitation",
            timeout=30,
            verify=False
        )
        assert accept_resp.status_code == 200, f"Accept invitation failed: {accept_resp.status_code} {accept_resp.text}"
        accept_data = accept_resp.json()
        assert "Message" in accept_data, "Confirmation message missing in accept-invitation response"
        assert isinstance(accept_data["Message"], str) and len(accept_data["Message"]) > 0, "Invalid confirmation message"

    finally:
        pass

test_post_api_organizations_memberships_membershipid_accept_invitation_accepts_invitation()
