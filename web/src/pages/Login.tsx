import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { describeError } from '@/api/client';
import { useAuth } from '@/lib/auth';
import { Button, Icon } from '@/components/ui';
import { BRAND_LOGO, PRODUCT_NAME } from '@/components/Brand';
import './login.css';

export function LoginPage() {
  const { signIn } = useAuth();
  const navigate = useNavigate();

  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      const user = await signIn(userName.trim(), password);

      // Land people in the portal that matches who they are rather than a generic home page.
      const destination =
        user.primaryPortal === 'admin' ? '/admin'
        : user.primaryPortal === 'teacher' ? '/teacher'
        : '/admin';

      navigate(destination, { replace: true });
    } catch (caught) {
      setError(describeError(caught));
      setPassword('');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="login-page">
      <main className="login-card">
        <header className="login-head">
          <img className="login-logo" src={BRAND_LOGO} alt={PRODUCT_NAME} />
          <h1>{PRODUCT_NAME}</h1>
          <p>Sign in with your school account to continue.</p>
        </header>

        <form className="login-form" onSubmit={handleSubmit}>
          {error && (
            <div className="alert alert-error" role="alert">
              <Icon name="alert" />
              <div>
                <div className="alert-title">Could not sign in</div>
                <div className="alert-body">{error}</div>
              </div>
            </div>
          )}

          <div className="field">
            <label className="label" htmlFor="username">Username or email</label>
            <input
              id="username"
              className="input"
              value={userName}
              onChange={(e) => setUserName(e.target.value)}
              autoComplete="username"
              autoFocus
              required
              placeholder="e.g. j.smith"
            />
          </div>

          <div className="field">
            <label className="label" htmlFor="password">Password</label>
            <div className="login-password">
              <input
                id="password"
                className="input"
                type={showPassword ? 'text' : 'password'}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
                required
              />
              <button
                type="button"
                className="login-password-toggle"
                onClick={() => setShowPassword((v) => !v)}
                aria-label={showPassword ? 'Hide password' : 'Show password'}
              >
                {showPassword ? 'Hide' : 'Show'}
              </button>
            </div>
          </div>

          <Button type="submit" variant="primary" size="lg" block loading={busy} icon="login">
            {busy ? 'Signing in' : 'Sign in'}
          </Button>
        </form>

        <p className="login-help">
          Trouble signing in? Contact the school office to have your password reset.
        </p>
      </main>
    </div>
  );
}
