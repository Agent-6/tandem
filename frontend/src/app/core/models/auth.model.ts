export interface User {
  id: string;
  email: string;
  displayName: string;
  avatarColor: string;
}

export interface AuthResponse {
  token: string;
  userId: string;
  displayName: string;
  email: string;
  avatarColor: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
}
