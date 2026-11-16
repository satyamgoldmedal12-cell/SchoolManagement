import React, { useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './styles.css';

function UsersWorkspace({ initial, apiUrl, csrf, accessEmailUrl, setActiveUrl, actorId }) {
  const [data, setData] = useState(initial);
  const [search, setSearch] = useState(initial.search ?? '');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const firstRowNumber = (data.page - 1) * 50 + 1;
  const pending = useRef(null);
  useEffect(() => () => pending.current?.abort(), []);
  async function load(page, query = search) {
    pending.current?.abort();
    const controller = new AbortController();
    pending.current = controller;
    setLoading(true); setError('');
    try {
      const url = new URL(apiUrl, window.location.origin);
      url.searchParams.set('page', page);
      if (query.trim()) url.searchParams.set('search', query.trim());
      const response = await fetch(url, { credentials: 'same-origin', signal: controller.signal, headers: { Accept: 'application/json' } });
      if (response.status === 401 || response.status === 403) throw new Error('Your session ended or access changed. Reload the page to sign in.');
      if (!response.ok) throw new Error('Users could not be loaded. Please try again.');
      setData(await response.json());
    } catch (failure) { if (failure.name !== 'AbortError') setError(failure.message); }
    finally { if (!controller.signal.aborted) setLoading(false); }
  }
  function Hidden({ row }) {
    return <><input type="hidden" name="__RequestVerificationToken" value={csrf} /><input type="hidden" name="id" value={row.id} /></>;
  }
  return <section className="school-users-shell tw:rounded-2xl tw:border tw:border-slate-200 tw:bg-white tw:p-4 tw:shadow-sm" aria-busy={loading}>
    <div className="tw:mb-4 tw:flex tw:flex-wrap tw:items-center tw:justify-between tw:gap-3">
      <div><p className="tw:mb-1 tw:text-xs tw:font-semibold tw:uppercase tw:tracking-widest tw:text-slate-500">People & access</p><strong>{data.items.length} users on this page</strong></div>
      <span className="badge text-bg-light">First login requires a private password</span>
    </div>
    <form className="tw:mb-4 tw:flex tw:gap-2" role="search" onSubmit={event => { event.preventDefault(); load(1); }}>
      <label className="visually-hidden" htmlFor="users-search">Search users</label>
      <input id="users-search" className="form-control" placeholder="Search by name or email" value={search} onChange={event => setSearch(event.target.value)} />
      <button className="btn btn-outline-primary" disabled={loading}>Search</button>
    </form>
    {error && <div className="alert alert-danger" role="alert">{error} <button className="btn btn-sm btn-link" onClick={() => load(data.page)}>Retry</button></div>}
    <div className="table-responsive"><table className="table align-middle">
      <thead><tr><th>Sr. No.</th><th>Name / email</th><th>Designation / role</th><th>Status</th><th>Last login (UTC)</th><th>Actions</th></tr></thead>
      <tbody>{data.items.map((row, index) => <tr key={row.id}>
        <td>{firstRowNumber + index}</td>
        <td><strong>{row.fullName}</strong><div className="tw:text-sm tw:text-slate-500">{row.email}</div></td>
        <td>{row.designationName ?? 'Unassigned'}<div className="tw:text-sm tw:text-slate-500">{row.roleName}</div></td>
        <td><span className={`badge ${row.isActive ? 'text-bg-success' : 'text-bg-secondary'}`}>{row.isActive ? 'Active' : 'Inactive'}</span><div className="tw:mt-1 tw:text-xs tw:text-slate-500">{row.mustChangePassword ? 'First password change pending' : 'Password set'}</div><div className="tw:text-xs tw:text-slate-500">{row.invitationSentAt ? 'Login email sent' : 'Email pending'}</div></td>
        <td className="tw:text-sm">{row.lastLoginAt ? row.lastLoginAt.replace('T', ' ').slice(0, 16) : 'Never'}</td>
        <td><div className="tw:flex tw:flex-wrap tw:gap-2">
          {row.isActive && <form action={accessEmailUrl} method="post"><Hidden row={row} /><button className="btn btn-sm btn-outline-primary">{row.mustChangePassword ? 'Resend login email' : 'Send reset link'}</button></form>}
          {row.id !== actorId && <form action={setActiveUrl} method="post"><Hidden row={row} /><input type="hidden" name="active" value={String(!row.isActive)} /><input type="hidden" name="rowVersion" value={row.rowVersion} /><button className="btn btn-sm btn-outline-secondary">{row.isActive ? 'Deactivate' : 'Activate'}</button></form>}
        </div></td>
      </tr>)}{data.items.length === 0 && <tr><td colSpan="6" className="tw:py-8 tw:text-center tw:text-slate-500">No users found. Try another search or add a user.</td></tr>}</tbody>
    </table></div>
    <nav className="tw:mt-3 tw:flex tw:items-center tw:gap-2" aria-label="User pages">
      <button className="btn btn-sm btn-outline-secondary" disabled={data.page <= 1 || loading} onClick={() => load(data.page - 1, data.search ?? '')}>Previous</button>
      <span className="tw:px-2 tw:text-sm" aria-live="polite">{loading ? 'Loading…' : `Page ${data.page}`}</span>
      <button className="btn btn-sm btn-outline-secondary" disabled={!data.hasNext || loading} onClick={() => load(data.page + 1, data.search ?? '')}>Next</button>
    </nav>
  </section>;
}

const mount = document.getElementById('users-workspace');
if (mount) {
  const { initial, apiUrl, csrf, accessEmailUrl, setActiveUrl, actorId } = mount.dataset;
  createRoot(mount).render(<UsersWorkspace initial={JSON.parse(initial)} {...{ apiUrl, csrf, accessEmailUrl, setActiveUrl, actorId }} />);
}
