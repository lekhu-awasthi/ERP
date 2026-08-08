import requests

BASE_URL = "http://localhost:5155"
REGISTER_ENDPOINT = "/api/auth/register"
HEADERS = {"Content-Type": "application/json"}
TIMEOUT = 30


def test_postapiauthregisterreturns400forinvalidormissingfields():
    url = BASE_URL + REGISTER_ENDPOINT

    # List of invalid test payloads with missing or invalid fields
    invalid_payloads = [
        # Missing fullName
        {"email": "user@example.com", "phone": "1234567890", "password": "validPass1"},
        # Missing email
        {"fullName": "Test User", "phone": "1234567890", "password": "validPass1"},
        # Missing phone
        {"fullName": "Test User", "email": "user@example.com", "password": "validPass1"},
        # Missing password
        {"fullName": "Test User", "email": "user@example.com", "phone": "1234567890"},
        # Invalid email (not an email format)
        {"fullName": "Test User", "email": "invalid-email", "phone": "1234567890", "password": "validPass1"},
        # Email too long (257 chars)
        {"fullName": "Test User", "email": "a" * 257 + "@example.com", "phone": "1234567890", "password": "validPass1"},
        # Phone too long (21 chars)
        {"fullName": "Test User", "email": "user@example.com", "phone": "1" * 21, "password": "validPass1"},
        # Password too short (7 chars)
        {"fullName": "Test User", "email": "user@example.com", "phone": "1234567890", "password": "short1"},
        # Password too long (101 chars)
        {
            "fullName": "Test User",
            "email": "user@example.com",
            "phone": "1234567890",
            "password": "p" * 101,
        },
        # All fields empty strings
        {"fullName": "", "email": "", "phone": "", "password": ""},
        # All fields null (not allowed by JSON schema but testing empty)
        {"fullName": None, "email": None, "phone": None, "password": None}
    ]

    for payload in invalid_payloads:
        response = requests.post(url, json=payload, headers=HEADERS, timeout=TIMEOUT)
        assert response.status_code == 400, (
            f"Expected 400 for payload {payload}, got {response.status_code}: {response.text}"
        )


test_postapiauthregisterreturns400forinvalidormissingfields()