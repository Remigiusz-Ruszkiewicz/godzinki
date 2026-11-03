-- Moja Praca: initial schema. Run once in the Supabase SQL Editor.
-- Contains no personal workbook data. Transaction rolls back on any failure.
begin;

create function public.mp_touch_row() returns trigger
language plpgsql set search_path = '' as $$
begin
  if new.user_id <> old.user_id or new.id <> old.id then
    raise exception 'Record identity and owner cannot be changed';
  end if;
  new.created_at := old.created_at;
  new.updated_at := clock_timestamp();
  new.version := old.version + 1;
  return new;
end;
$$;
revoke all on function public.mp_touch_row() from public;

create table public.mp_work_days (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null default auth.uid() references auth.users(id),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  version bigint not null default 1 check (version > 0),
  deleted_at timestamptz,
  work_date date not null,
  start_time time, end_time time,
  ends_next_day boolean not null default false,
  break_minutes integer not null default 0 check (break_minutes between 0 and 1440),
  worked_minutes integer check (worked_minutes between 0 and 2880),
  norm_minutes integer not null default 480 check (norm_minutes between 0 and 1440),
  overtime_minutes_override integer,
  day_type text not null default 'work' check (day_type in ('work','leave','sick','holiday','off','business_trip')),
  category text, notes text,
  unique (user_id, work_date),
  unique (user_id, id)
);
alter table public.mp_work_days enable row level security;
revoke all on public.mp_work_days from anon, authenticated;
grant select, insert, update on public.mp_work_days to authenticated;
create policy own_rows on public.mp_work_days for all to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);
create index mp_work_days_sync on public.mp_work_days (user_id, updated_at, id);
create trigger touch_row before update on public.mp_work_days
  for each row execute function public.mp_touch_row();

create table public.mp_extra_work (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null default auth.uid() references auth.users(id),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  version bigint not null default 1 check (version > 0),
  deleted_at timestamptz,
  work_date date not null,
  minutes integer not null check (minutes > 0),
  project_name text, category text, notes text,
  unique (user_id, id)
);
alter table public.mp_extra_work enable row level security;
revoke all on public.mp_extra_work from anon, authenticated;
grant select, insert, update on public.mp_extra_work to authenticated;
create policy own_rows on public.mp_extra_work for all to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);
create index mp_extra_work_sync on public.mp_extra_work (user_id, updated_at, id);
create trigger touch_row before update on public.mp_extra_work
  for each row execute function public.mp_touch_row();

create table public.mp_pay_rates (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null default auth.uid() references auth.users(id),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  version bigint not null default 1 check (version > 0),
  deleted_at timestamptz,
  effective_from date not null,
  base_net numeric(12,2) not null check (base_net >= 0),
  hourly_net numeric(12,2) not null check (hourly_net >= 0),
  currency text not null default 'PLN' check (currency = 'PLN'),
  notes text,
  unique (user_id, effective_from),
  unique (user_id, id)
);
alter table public.mp_pay_rates enable row level security;
revoke all on public.mp_pay_rates from anon, authenticated;
grant select, insert, update on public.mp_pay_rates to authenticated;
create policy own_rows on public.mp_pay_rates for all to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);
create index mp_pay_rates_sync on public.mp_pay_rates (user_id, updated_at, id);
create trigger touch_row before update on public.mp_pay_rates
  for each row execute function public.mp_touch_row();

create table public.mp_settlements (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null default auth.uid() references auth.users(id),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  version bigint not null default 1 check (version > 0),
  deleted_at timestamptz,
  month date not null check (extract(day from month) = 1),
  status text not null default 'draft' check (status in ('draft','approved')),
  company_minutes_snapshot integer,
  extra_minutes_snapshot integer check (extra_minutes_snapshot >= 0),
  approved_minutes integer check (approved_minutes >= 0),
  base_net_snapshot numeric(12,2) check (base_net_snapshot >= 0),
  hourly_net_snapshot numeric(12,2) check (hourly_net_snapshot >= 0),
  bonus_net numeric(12,2) not null default 0,
  correction_net numeric(12,2) not null default 0,
  total_net numeric(12,2) generated always as
    (round(base_net_snapshot + approved_minutes::numeric / 60 * hourly_net_snapshot + bonus_net + correction_net, 2)) stored,
  calculation_basis text not null default 'separate_entries'
    check (calculation_basis in ('separate_entries','legacy_excel','manual')),
  correction_reason text, notes text, approved_at timestamptz,
  check (status <> 'approved' or
    (approved_minutes is not null and base_net_snapshot is not null and hourly_net_snapshot is not null and approved_at is not null)),
  unique (user_id, month),
  unique (user_id, id)
);
alter table public.mp_settlements enable row level security;
revoke all on public.mp_settlements from anon, authenticated;
grant select, insert, update on public.mp_settlements to authenticated;
create policy own_rows on public.mp_settlements for all to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);
create index mp_settlements_sync on public.mp_settlements (user_id, updated_at, id);
create trigger touch_row before update on public.mp_settlements
  for each row execute function public.mp_touch_row();

create table public.mp_payments (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null default auth.uid() references auth.users(id),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  version bigint not null default 1 check (version > 0),
  deleted_at timestamptz,
  settlement_id uuid not null,
  paid_on date not null,
  amount_net numeric(12,2) not null check (amount_net > 0),
  notes text,
  unique (user_id, id)
);
alter table public.mp_payments enable row level security;
revoke all on public.mp_payments from anon, authenticated;
grant select, insert, update on public.mp_payments to authenticated;
create policy own_rows on public.mp_payments for all to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);
create index mp_payments_sync on public.mp_payments (user_id, updated_at, id);
create trigger touch_row before update on public.mp_payments
  for each row execute function public.mp_touch_row();

create table public.mp_leave_ledger (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null default auth.uid() references auth.users(id),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  version bigint not null default 1 check (version > 0),
  deleted_at timestamptz,
  effective_date date not null,
  entitlement_year integer not null check (entitlement_year between 2000 and 2200),
  kind text not null check (kind in ('allocation','carryover','taken','correction')),
  days_delta numeric(6,2) not null check (days_delta <> 0),
  notes text,
  check ((kind = 'taken' and days_delta < 0) or (kind in ('allocation','carryover') and days_delta > 0) or kind = 'correction'),
  unique (user_id, id)
);
alter table public.mp_leave_ledger enable row level security;
revoke all on public.mp_leave_ledger from anon, authenticated;
grant select, insert, update on public.mp_leave_ledger to authenticated;
create policy own_rows on public.mp_leave_ledger for all to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);
create index mp_leave_ledger_sync on public.mp_leave_ledger (user_id, updated_at, id);
create trigger touch_row before update on public.mp_leave_ledger
  for each row execute function public.mp_touch_row();

create table public.mp_goals (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null default auth.uid() references auth.users(id),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  version bigint not null default 1 check (version > 0),
  deleted_at timestamptz,
  name text not null check (length(trim(name)) > 0),
  starts_on date not null, ends_on date not null,
  target_net numeric(12,2) not null check (target_net > 0),
  basis text not null default 'earned' check (basis in ('earned','paid')),
  check (ends_on >= starts_on),
  unique (user_id, id)
);
alter table public.mp_goals enable row level security;
revoke all on public.mp_goals from anon, authenticated;
grant select, insert, update on public.mp_goals to authenticated;
create policy own_rows on public.mp_goals for all to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);
create index mp_goals_sync on public.mp_goals (user_id, updated_at, id);
create trigger touch_row before update on public.mp_goals
  for each row execute function public.mp_touch_row();

create table public.mp_import_rows (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null default auth.uid() references auth.users(id),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  version bigint not null default 1 check (version > 0),
  deleted_at timestamptz,
  source_fingerprint text not null,
  sheet_name text not null, row_number integer not null check (row_number > 0),
  raw_values jsonb not null,
  review_status text not null default 'pending' check (review_status in ('pending','accepted','needs_review','ignored')),
  review_notes text,
  unique (user_id, source_fingerprint, sheet_name, row_number),
  unique (user_id, id)
);
alter table public.mp_import_rows enable row level security;
revoke all on public.mp_import_rows from anon, authenticated;
grant select, insert, update on public.mp_import_rows to authenticated;
create policy own_rows on public.mp_import_rows for all to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);
create index mp_import_rows_sync on public.mp_import_rows (user_id, updated_at, id);
create trigger touch_row before update on public.mp_import_rows
  for each row execute function public.mp_touch_row();

-- Composite FK prevents attaching a payment to another user's settlement.
alter table public.mp_payments add constraint mp_payments_owned_settlement
  foreign key (user_id, settlement_id) references public.mp_settlements(user_id, id);

-- API deletes are intentionally not granted. Use deleted_at so offline clients
-- can receive tombstones; business queries must exclude deleted_at IS NOT NULL.
-- Updates must filter by BOTH id and the previously read version to detect conflicts.
commit;

