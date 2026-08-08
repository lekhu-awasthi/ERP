import requests

def test_postapiauthrequestverificationcodereturns404forunknownemail():
    base_url = "http://localhost:5155"
    endpoint = "/api/auth/request-verification-code"
    url = base_url + endpoint

    headers = {
        "Content-Type": "application/json"
    }
    # Use an email that is presumably not registered to trigger 404
    payload = {
        "email": "unknown.email.for.testing@example.com"
    }

    try:
        response = requests.post(url, json=payload, headers=headers, timeout=30)
    except requests.RequestException as e:
        assert False, f"Request failed: {e}"

    assert response.status_code == 404, f"Expected status code 404 but got {response.status_code}"
    # Optionally check response body for exact 'no account found' indication
    # The PRD says 404 no account found, but does not specify exact body schema
    # So we just check that response content indicates no account found (if any)
    content = response.json() if response.content else {}
    # It's possible the error message is in some field, we can check common keys
    expected_message_lower = "no account found"
    message_found = any(expected_message_lower in str(v).lower() for v in content.values()) if isinstance(content, dict) else False
    # It's acceptable if no message, so no assert here, just leave it

test_postapiauthrequestverificationcodereturns404forunknownemail()