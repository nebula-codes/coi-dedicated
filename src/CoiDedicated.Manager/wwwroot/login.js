'use strict';

const loginForm = document.getElementById('login-form');
const loginError = document.getElementById('login-error');
const loginButton = document.getElementById('login-submit');
fetch('/api/auth/session', { credentials: 'same-origin', cache: 'no-store' })
  .then(response => response.ok ? response.json() : null)
  .then(session => { if (session?.authenticated) location.replace('/'); })
  .catch(() => {});
loginForm.addEventListener('submit', async event => {
  event.preventDefault();
  loginError.hidden = true; loginButton.disabled = true; loginButton.textContent = 'Signing in…';
  try {
    const response = await fetch('/api/auth/login', {
      method: 'POST', credentials: 'same-origin', cache: 'no-store',
      headers: { 'Content-Type': 'application/json', 'X-Manager-Request': '1' },
      body: JSON.stringify({ token: document.getElementById('admin-token').value.trim() })
    });
    const result = await response.json().catch(() => null);
    if (!response.ok) throw new Error(response.status === 429 ? 'Too many sign-in attempts. Wait one minute and try again.' : result?.error || `Sign-in failed (${response.status}).`);
    document.getElementById('admin-token').value = '';
    location.replace('/');
  } catch (error) {
    loginError.textContent = error.message === 'Failed to fetch' ? 'The manager could not be reached. Check that the stack is running and try again.' : error.message;
    loginError.hidden = false;
  } finally { loginButton.disabled = false; loginButton.textContent = 'Sign in'; }
});
