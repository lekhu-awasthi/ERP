import requests
import uuid

base_url = "http://localhost:5155"
register_url = f"{base_url}/api/auth/register"
timeout = 30
headers = {"Content-Type": "application/json"}


def test_postapiauthregisterreturns409forduplicateemail():
    # Prepare user data for registration
    user_data = {
        "fullName": "Test User",
        "email": f"testuser_{uuid.uuid4()}@example.com",
        "phone": "1234567890",
        "password": "StrongPassw0rd!"
    }

    # Register new user (expected 201)
    response = requests.post(register_url, json=user_data, headers=headers, timeout=timeout)
    assert response.status_code == 201, f"Setup user registration failed with status {response.status_code}: {response.text}"
    created_user_id = None
    try:
        json_resp = response.json()
        assert "userId" in json_resp and "email" in json_resp
        created_user_id = json_resp["userId"]
        # Attempt to register again with the same email to trigger conflict
        duplicate_response = requests.post(register_url, json=user_data, headers=headers, timeout=timeout)
        assert duplicate_response.status_code == 409, f"Expected 409 Conflict but got {duplicate_response.status_code}: {duplicate_response.text}"
    finally:
        if created_user_id:
            # No delete endpoint is specified in the PRD so resource cleanup cannot be done here
            # Leaving cleanup comment for future implementation if API exposes user deletion
            pass


test_postapiauthregisterreturns409forduplicateemail()