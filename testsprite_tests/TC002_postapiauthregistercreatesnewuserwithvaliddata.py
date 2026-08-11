import requests
import uuid

BASE_URL = "http://localhost:5155"

def test_post_api_auth_register_creates_new_user_with_valid_data():
    url = f"{BASE_URL}/api/auth/register"
    # Generate a unique email to avoid conflicts in repeated test runs
    unique_email = f"testuser_{uuid.uuid4().hex[:8]}@example.com"
    payload = {
        "fullName": "Test User",
        "email": unique_email,
        "phone": "+12345678901",
        "password": "SecurePass123!"
    }
    headers = {
        "Content-Type": "application/json"
    }

    response = requests.post(url, json=payload, headers=headers, timeout=30)
    try:
        # Assert status code 201 Created
        assert response.status_code == 201, f"Expected status code 201, got {response.status_code}"
        data = response.json()
        # Validate response fields
        assert "userId" in data, "Response missing 'userId'"
        assert "email" in data, "Response missing 'email'"
        # userId should be a valid UUID
        try:
            uuid.UUID(data["userId"])
        except ValueError:
            assert False, f"userId is not a valid UUID: {data['userId']}"
        # Email returned should match the registered email
        assert data["email"].lower() == unique_email.lower(), f"Returned email does not match: {data['email']}"
    finally:
        # Cleanup: Normally, if the API supported user deletion, we would delete created user here.
        # Since no delete endpoint is provided in the PRD for auth users, no cleanup is done.
        pass

test_post_api_auth_register_creates_new_user_with_valid_data()
