-- Disposable integration tests: all test records are rolled back.
begin;
select set_config('mp.test_user_a', gen_random_uuid()::text, true);
select set_config('mp.test_user_b', gen_random_uuid()::text, true);
insert into auth.users (id) values
(current_setting('mp.test_user_a')::uuid), (current_setting('mp.test_user_b')::uuid);
set local role authenticated;
select set_config('request.jwt.claim.sub', current_setting('mp.test_user_a'), true);
do $$
declare record_id uuid; settlement_id uuid; amount numeric; v bigint; n integer;
begin
  insert into public.mp_work_days (work_date, worked_minutes)
  values ('2099-01-02', 540) returning id into record_id;
  update public.mp_work_days set worked_minutes=550 where id=record_id and version=1;
  select version into v from public.mp_work_days where id=record_id;
  if v <> 2 then raise exception 'Version increment failed'; end if;
  update public.mp_work_days set worked_minutes=560 where id=record_id and version=1;
  get diagnostics n = row_count;
  if n <> 0 then raise exception 'Stale update not rejected'; end if;
  insert into public.mp_settlements
    (month, approved_minutes, base_net_snapshot, hourly_net_snapshot, status, approved_at)
  values ('2099-01-01', 1230, 10000, 88, 'approved', now())
  returning id, total_net into settlement_id, amount;
  if amount <> 11804 then raise exception 'Net calculation failed: %', amount; end if;
  perform set_config('request.jwt.claim.sub', current_setting('mp.test_user_b'), true);
  if exists(select 1 from public.mp_work_days where id=record_id) then
    raise exception 'Cross-user read allowed';
  end if;
  update public.mp_work_days set worked_minutes=600 where id=record_id;
  get diagnostics n = row_count;
  if n <> 0 then raise exception 'Cross-user update allowed'; end if;
  begin
    insert into public.mp_work_days (user_id, work_date) values
      (current_setting('mp.test_user_a')::uuid, '2099-01-03');
    raise exception 'Owner spoof allowed';
  exception when insufficient_privilege then null;
  end;
  begin
    insert into public.mp_payments (settlement_id, paid_on, amount_net)
    values (settlement_id, '2099-01-31', 100);
    raise exception 'Cross-user settlement link allowed';
  exception when foreign_key_violation then null;
  end;
end;
$$;
set local role anon;
do $$
begin
  begin
    perform 1 from public.mp_work_days;
    raise exception 'Anonymous read allowed';
  exception when insufficient_privilege then null;
  end;
end;
$$;
reset role;
rollback;
select 'PASS' as result,
'Own writes, isolated users, anonymous denial, payment ownership, net calculation, version conflicts; test data rolled back' as checked;

