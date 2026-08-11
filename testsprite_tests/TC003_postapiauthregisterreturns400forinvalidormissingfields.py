import requests
import urllib3

# Disable warnings about insecure requests
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

def test_post_api_auth_register_returns_400_for_invalid_or_missing_fields():
    base_url = "http://localhost:5155"
    url = f"{base_url}/api/auth/register"
    headers = {"Content-Type": "application/json"}

    test_payloads = [
        {},  # completely empty body
        {"fullName": "Test User"},  # missing email, phone, password
        {"email": "invalid-email", "fullName": "Test User", "phone": "1234567890", "password": "password123"},  # invalid email
        {"fullName": "Test User", "email": "test@example.com", "phone": "123456789012345678901234567890", "password": "password123"},  # phone too long (>20 chars)
        {"fullName": "Test User", "email": "test@example.com", "phone": "1234567890", "password": "short"},  # password too short (<8 chars)
        {"fullName": "", "email": "test@example.com", "phone": "1234567890", "password": "password123"},  # empty fullName
        {"fullName": "Test User", "email": "", "phone": "1234567890", "password": "password123"},  # empty email
        {"fullName": "Test User", "email": "test@example.com", "phone": "", "password": "password123"},  # empty phone
        {"fullName": "Test User", "email": "test@example.com", "phone": "1234567890", "password": ""},  # empty password
        {"fullName": "Test User", "email": "a"*257 + "@example.com", "phone": "1234567890", "password": "password123"}  # email too long (>256 chars)
    ]

    for payload in test_payloads:
        try:
            response = requests.post(url, json=payload, headers=headers, timeout=30, verify=False)
        except Exception as e:
            assert False, f"Request failed with exception: {e}"
        else:
            # Assert HTTP 400 Bad Request for all invalid/missing field cases
            assert response.status_code == 400, f"Expected status 400 but got {response.status_code} for payload: {payload}"

test_post_api_auth_register_returns_400_for_invalid_or_missing_fields()