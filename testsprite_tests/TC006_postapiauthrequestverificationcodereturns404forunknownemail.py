import requests

def test_post_api_auth_request_verification_code_returns_404_for_unknown_email():
    base_url = "http://localhost:5155"
    endpoint = "/api/auth/request-verification-code"
    url = f"{base_url}{endpoint}"
    unknown_email = "unknown.email@example.com"
    payload = {"email": unknown_email}
    headers = {"Content-Type": "application/json"}

    try:
        response = requests.post(url, json=payload, headers=headers, timeout=30, verify=False)
    except requests.RequestException as e:
        assert False, f"Request failed: {e}"

    assert response.status_code == 404, f"Expected status code 404, got {response.status_code}"
    # Response body should indicate no account found - we expect JSON with error or message but not specified
    try:
        data = response.json()
    except ValueError:
        assert False, "Response is not valid JSON"

    # We expect some indication of no account found in the response message or error
    # Since schema specifies "404": "No account found with this email"
    # The actual response detail is not explicitly defined; just check for presence of relevant info
    found_relevant_text = any(
        keyword in data.get(key, "").lower()
        for key in data
        for keyword in ["no account found", "not found", "404"]
    )
    assert found_relevant_text, f"Response JSON does not indicate no account found: {data}"


test_post_api_auth_request_verification_code_returns_404_for_unknown_email()