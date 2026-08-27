export interface RegisterRequest {
  fullName: string;
  email: string;
  phone: string;
  password: string;
  turnstileToken: string;
}

export interface RegisterResponse {
  userId: string;
  email: string;
}

export interface CurrentUser {
  userId: string;
  email: string;
  fullName?: string;
}
