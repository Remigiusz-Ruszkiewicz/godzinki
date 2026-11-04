-- Run once in the project's SQL Editor. No personal data or credentials.
begin;
create table public.mp_bonuses (
 id uuid primary key default gen_random_uuid(), user_id uuid not null default auth.uid() references auth.users(id),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
 version bigint not null default 1, deleted_at timestamptz,
 month date not null check(extract(day from month)=1), name text not null check(length(trim(name))>0),
 amount_net numeric(12,2) not null check(amount_net>0), notes text,
 unique(user_id,id)
);
create table public.mp_trips (
 id uuid primary key default gen_random_uuid(), user_id uuid not null default auth.uid() references auth.users(id),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
 version bigint not null default 1, deleted_at timestamptz,
 destination text not null check(length(trim(destination))>0), country text not null default 'Polska', purpose text,
 transport text not null, start_date date not null, start_time time not null, end_date date not null, end_time time not null,
 daily_rate numeric(12,2) not null default 0 check(daily_rate>=0),
 allowance_amount numeric(12,2) not null default 0 check(allowance_amount>=0), currency text not null default 'PLN' check(currency ~ '^[A-Z]{3}$'),
 notes text, check(end_date+end_time>=start_date+start_time), unique(user_id,id)
);
alter table public.mp_bonuses enable row level security;
alter table public.mp_trips enable row level security;
revoke all on public.mp_bonuses,public.mp_trips from anon,authenticated;
grant select,insert,update on public.mp_bonuses,public.mp_trips to authenticated;
create policy own_rows on public.mp_bonuses for all to authenticated using((select auth.uid())=user_id) with check((select auth.uid())=user_id);
create policy own_rows on public.mp_trips for all to authenticated using((select auth.uid())=user_id) with check((select auth.uid())=user_id);
create trigger touch_row before update on public.mp_bonuses for each row execute function public.mp_touch_row();
create trigger touch_row before update on public.mp_trips for each row execute function public.mp_touch_row();
create index mp_bonuses_month on public.mp_bonuses(user_id,month);
create index mp_trips_date on public.mp_trips(user_id,start_date);
alter table public.mp_work_days add column trip_id uuid;
alter table public.mp_work_days add constraint mp_work_days_owned_trip foreign key(user_id,trip_id) references public.mp_trips(user_id,id);

-- One atomic save for the trip and its daily work entries. Runs with caller RLS.
create function public.mp_save_trip(p_trip jsonb,p_days jsonb,p_previous jsonb) returns uuid
language plpgsql security invoker set search_path='' as $$
declare
 t public.mp_trips; d public.mp_work_days; item jsonb; old_day public.mp_work_days;
 owner_id uuid:=auth.uid(); trip_key uuid:=(p_trip->>'id')::uuid; expected bigint:=(p_trip->>'version')::bigint;
begin
 if owner_id is null then raise exception 'Zaloguj się ponownie.'; end if;
 if jsonb_typeof(p_days)<>'array' or jsonb_typeof(p_previous)<>'array' then raise exception 'Nieprawidłowe dni pracy.'; end if;
 t:=jsonb_populate_record(null::public.mp_trips,p_trip);
 if expected>0 then
  perform 1 from public.mp_trips where id=trip_key and user_id=owner_id and version=expected and deleted_at is null for update;
  if not found then raise exception 'Delegacja została zmieniona. Odśwież dane.'; end if;
 end if;
 -- Reject stale edits, including daily changes made through the calendar.
 for old_day in select * from public.mp_work_days where user_id=owner_id and trip_id=trip_key and deleted_at is null for update loop
  if not exists(select 1 from jsonb_array_elements(p_previous) x where (x->>'id')::uuid=old_day.id and (x->>'version')::bigint=old_day.version) then
   raise exception 'Dzień delegacji zmieniono w kalendarzu. Odśwież dane.';
  end if;
 end loop;
 if exists(select 1 from jsonb_array_elements(p_previous) x where not exists(select 1 from public.mp_work_days w where w.user_id=owner_id and w.trip_id=trip_key and w.id=(x->>'id')::uuid and w.version=(x->>'version')::bigint and w.deleted_at is null)) then
  raise exception 'Lista dni delegacji uległa zmianie. Odśwież dane.';
 end if;
 if expected=0 then
  insert into public.mp_trips(id,user_id,destination,country,purpose,transport,start_date,start_time,end_date,end_time,daily_rate,allowance_amount,currency,notes)
  values(trip_key,owner_id,t.destination,t.country,t.purpose,t.transport,t.start_date,t.start_time,t.end_date,t.end_time,t.daily_rate,t.allowance_amount,t.currency,t.notes);
 else
  update public.mp_trips set destination=t.destination,country=t.country,purpose=t.purpose,transport=t.transport,start_date=t.start_date,start_time=t.start_time,end_date=t.end_date,end_time=t.end_time,daily_rate=t.daily_rate,allowance_amount=t.allowance_amount,currency=t.currency,notes=t.notes,deleted_at=t.deleted_at where id=trip_key and user_id=owner_id;
 end if;
 if t.deleted_at is not null then
  update public.mp_work_days set deleted_at=now() where user_id=owner_id and trip_id=trip_key and deleted_at is null;
  return trip_key;
 end if;
 if exists(select 1 from jsonb_array_elements(p_days) x group by x->>'work_date' having count(*)>1) then raise exception 'Dzień pracy wpisano dwukrotnie.'; end if;
 for item in select * from jsonb_array_elements(p_days) loop
  d:=jsonb_populate_record(null::public.mp_work_days,item);
  if d.work_date<t.start_date or d.work_date>t.end_date or d.worked_minutes is null then raise exception 'Dzień pracy musi mieścić się w terminie delegacji i mieć podany czas.'; end if;
  select * into old_day from public.mp_work_days where user_id=owner_id and work_date=d.work_date for update;
  if found then
   if old_day.deleted_at is null and old_day.trip_id is distinct from trip_key then raise exception 'Dzień % ma już wpis. Popraw istniejący wpis w kalendarzu lub usuń ten dzień z delegacji.',d.work_date; end if;
   update public.mp_work_days set trip_id=trip_key,worked_minutes=d.worked_minutes,norm_minutes=d.norm_minutes,day_type='business_trip',notes=d.notes,
    start_time=null,end_time=null,ends_next_day=false,break_minutes=0,overtime_minutes_override=null,deleted_at=null where id=old_day.id;
  else
   insert into public.mp_work_days(user_id,trip_id,work_date,worked_minutes,norm_minutes,day_type,notes)
   values(owner_id,trip_key,d.work_date,d.worked_minutes,d.norm_minutes,'business_trip',d.notes);
  end if;
 end loop;
 update public.mp_work_days w set deleted_at=now() where w.user_id=owner_id and w.trip_id=trip_key and w.deleted_at is null
  and not exists(select 1 from jsonb_array_elements(p_days) x where (x->>'work_date')::date=w.work_date);
 return trip_key;
end;
$$;
revoke all on function public.mp_save_trip(jsonb,jsonb,jsonb) from public,anon;
grant execute on function public.mp_save_trip(jsonb,jsonb,jsonb) to authenticated;
notify pgrst,'reload schema';
commit;
