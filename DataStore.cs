using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;
namespace Godzinki;

public sealed class DataStore(HttpClient http, IConfiguration config, IJSRuntime js) {
 public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
 public Session? Session {get;private set;}
 public bool Demo {get;private set;}
 public bool SignedIn => Demo || Session is not null;
 public bool Loading {get;private set;}
 public string? LoadError {get;private set;}
 public List<WorkDay> Days {get;private set;} = [];
 public List<ExtraWork> Extras {get;private set;} = [];
 public List<PayRate> Rates {get;private set;} = [];
 public List<Settlement> Settlements {get;private set;} = [];
 public List<Payment> Payments {get;private set;} = [];
 public List<LeaveEntry> Leaves {get;private set;} = [];
 public List<Goal> Goals {get;private set;} = [];
 readonly SemaphoreSlim refreshLock = new(1,1);
 string Url => config["Supabase:Url"]!.TrimEnd('/');
 string Key => config["Supabase:PublishableKey"]!;
 public async Task Initialize() {
  var redirect=await js.InvokeAsync<string?>("godzinki.takeAuthHash");
  if (!string.IsNullOrEmpty(redirect)) {
   var query=redirect.TrimStart('#').Split('&').Select(x=>x.Split('=',2)).Where(x=>x.Length==2).ToDictionary(x=>x[0],x=>Uri.UnescapeDataString(x[1]));
   if(query.TryGetValue("access_token",out var access) && query.TryGetValue("refresh_token",out var refresh)) {
    using var req=Make(HttpMethod.Get,"/auth/v1/user"); req.Headers.Authorization=new("Bearer",access);
    using var res=await http.SendAsync(req); await Ensure(res);
    var user=await res.Content.ReadFromJsonAsync<UserInfo>(Json) ?? throw new Exception("Brak konta w odpowiedzi.");
    Session=new(){AccessToken=access,RefreshToken=refresh,ExpiresAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+3600,User=user};
    await PersistSession();
   } else if(query.TryGetValue("error_description",out var error)) throw new Exception(error);
  }
  if(Session is null) {
   var stored=await js.InvokeAsync<string?>("localStorage.getItem","godzinki.session");
   if(!string.IsNullOrEmpty(stored)) {
    try { Session=JsonSerializer.Deserialize<Session>(stored,Json); } catch(JsonException) { await js.InvokeVoidAsync("localStorage.removeItem","godzinki.session"); }
   }
  }
  if(Session is not null) await Reload();
 }
 HttpRequestMessage Make(HttpMethod method,string path,object? body=null) {
  var req=new HttpRequestMessage(method,Url+path);
  req.Headers.Add("apikey",Key);
  if(body is not null) req.Content=JsonContent.Create(body,options:Json);
  return req;
 }
 static async Task Ensure(HttpResponseMessage res) {
  if(res.IsSuccessStatusCode) return;
  var text=await res.Content.ReadAsStringAsync();
  JsonNode? error=null; try {error=JsonNode.Parse(text);}catch(JsonException){}
  var code=error?["code"]?.ToString();
  var msg=error?["msg"]?.ToString() ?? error?["message"]?.ToString() ?? error?["error_description"]?.ToString();
  if(code=="23505") throw new Exception("Taki dzień, miesiąc lub data stawki już istnieje. Odśwież dane i edytuj istniejący wpis.");
  if(code=="invalid_credentials") throw new Exception("Nieprawidłowy e-mail lub hasło.");
  if(code=="email_not_confirmed") throw new Exception("Najpierw potwierdź adres e-mail, klikając link w wiadomości.");
  throw new Exception(msg ?? $"Nie udało się połączyć z bazą ({(int)res.StatusCode}). Spróbuj ponownie.");
 }
 public async Task Login(string email,string password) {
  using var req=Make(HttpMethod.Post,"/auth/v1/token?grant_type=password",new {email,password});
  using var res=await http.SendAsync(req); await Ensure(res);
  Session=await res.Content.ReadFromJsonAsync<Session>(Json) ?? throw new Exception("Nie udało się zalogować.");
  Session.ExpiresAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+Session.ExpiresIn;
  Demo=false; await PersistSession(); await Reload();
 }
 public async Task Register(string email,string password,string redirect) {
  using var req=Make(HttpMethod.Post,"/auth/v1/signup?redirect_to="+Uri.EscapeDataString(redirect),new {email,password});
  using var res=await http.SendAsync(req); await Ensure(res);
 }
 async Task PersistSession() => await js.InvokeVoidAsync("localStorage.setItem","godzinki.session",JsonSerializer.Serialize(Session,Json));
 async Task Token() {
  if(Session is null) throw new Exception("Zaloguj się, aby zapisać dane.");
  await refreshLock.WaitAsync();
  try {
   if(Session.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds()+90) return;
   using var req=Make(HttpMethod.Post,"/auth/v1/token?grant_type=refresh_token",new {refresh_token=Session.RefreshToken});
   using var res=await http.SendAsync(req); await Ensure(res);
   Session=await res.Content.ReadFromJsonAsync<Session>(Json) ?? throw new Exception("Zaloguj się ponownie.");
   Session.ExpiresAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+Session.ExpiresIn;
   await PersistSession();
  } finally {refreshLock.Release();}
 }
 public async Task Logout() {
  if(Session is not null) {
   try {using var req=Make(HttpMethod.Post,"/auth/v1/logout");req.Headers.Authorization=new("Bearer",Session.AccessToken); using var res=await http.SendAsync(req);} catch(HttpRequestException){}
  }
  Session=null; Demo=false; LoadError=null;
  Days=[]; Extras=[];Rates=[];Settlements=[];Payments=[];Leaves=[];Goals=[];
  await js.InvokeVoidAsync("localStorage.removeItem","godzinki.session");
 }
 public async Task Reload() {
  if(Demo)return;
  Loading=true; LoadError=null;
  try {
   await Token();
   // Replace only after all requests succeed, avoiding a partially refreshed dashboard.
   var days=await Read<WorkDay>("work_days"); var extras=await Read<ExtraWork>("extra_work");
   var rates=await Read<PayRate>("pay_rates"); var settlements=await Read<Settlement>("settlements");
   var payments=await Read<Payment>("payments"); var leaves=await Read<LeaveEntry>("leave_ledger"); var goals=await Read<Goal>("goals");
   Days=days;Extras=extras;Rates=rates;Settlements=settlements;Payments=payments;Leaves=leaves;Goals=goals;
  } catch(Exception e) {LoadError=e is HttpRequestException?"Brak połączenia. Nie pobrano aktualnych danych; sprawdź internet i odśwież.":e.Message;}
  finally {Loading=false;}
 }
 async Task<List<T>> Read<T>(string table) {
  var all=new List<T>();
  for(int offset=0;;offset+=500) {
   using var req=Make(HttpMethod.Get,$"/rest/v1/mp_{table}?select=*&deleted_at=is.null&order=id&limit=500&offset={offset}");
   req.Headers.Authorization=new("Bearer",Session!.AccessToken);
   using var res=await http.SendAsync(req);await Ensure(res);
   var rows=await res.Content.ReadFromJsonAsync<List<T>>(Json) ?? [];
   all.AddRange(rows); if(rows.Count<500) return all;
  }
 }
 public async Task Save<T>(string table,T row) where T:Row {
  if(Demo) { DemoSave(row); return; }
  if(LoadError is not null) throw new Exception("Odśwież dane przed zapisem. Poprzednie pobranie nie powiodło się.");
  if(!await js.InvokeAsync<bool>("godzinki.online")) throw new Exception("Zapis wymaga internetu. Formularz pozostaje otwarty — spróbuj ponownie po połączeniu.");
  await Token();
  if(row.Version==0) {
   string? naturalKey=row switch {
    WorkDay w => $"work_date=eq.{w.WorkDate:yyyy-MM-dd}",
    PayRate r => $"effective_from=eq.{r.EffectiveFrom:yyyy-MM-dd}",
    Settlement s => $"month=eq.{s.Month:yyyy-MM-dd}", _=>null };
   if(naturalKey is not null) {
    using var lookup=Make(HttpMethod.Get,$"/rest/v1/mp_{table}?select=*&deleted_at=not.is.null&{naturalKey}&limit=1");
    lookup.Headers.Authorization=new("Bearer",Session!.AccessToken);
    using var found=await http.SendAsync(lookup);await Ensure(found);
    var oldRows=await found.Content.ReadFromJsonAsync<List<T>>(Json);
    if(oldRows?.Count>0){row.Id=oldRows[0].Id;row.Version=oldRows[0].Version;}
   }
  }
  var body=JsonSerializer.SerializeToNode(row,Json)!.AsObject();
  body.Remove("version");body.Remove("total_net");
  body["user_id"]=Session!.User.Id.ToString();
  var existing=row.Version>0;
  var path=$"/rest/v1/mp_{table}"+(existing?$"?id=eq.{row.Id}&version=eq.{row.Version}":"");
  using var req=Make(existing?HttpMethod.Patch:HttpMethod.Post,path,body);
  req.Headers.Authorization=new("Bearer",Session.AccessToken);req.Headers.Add("Prefer","return=representation");
  using var res=await http.SendAsync(req);await Ensure(res);
  var saved=await res.Content.ReadFromJsonAsync<List<T>>(Json);
  if(saved is null || saved.Count==0) throw new Exception("Ten wpis zmieniono na innym urządzeniu. Zamknij formularz, odśwież dane i sprawdź aktualną wersję przed ponownym zapisem.");
  // Update local record before reload, so a failed refresh cannot turn a successful write into a duplicate insert.
  Replace(saved[0]); row.Version=saved[0].Version;
  await Reload();
 }
 public async Task Delete<T>(string table,T row) where T:Row {
  var copy=Clone(row); copy.DeletedAt=DateTimeOffset.UtcNow; await Save(table,copy);
 }
 public async Task ImportHistory(JsonObject package,Func<string,Task> progress) {
  if(Demo || Session is null) throw new Exception("Zaloguj się na swoje konto, aby przenieść historię.");
  if(package["format"]?.GetValue<string>()!="godzinki-migration-v1" || package["tables"] is not JsonObject tables)
   throw new Exception("Wybierz przygotowany plik migracji Godzinek.");
  if(!string.Equals(package["owner_email"]?.GetValue<string>(),Session.User.Email,StringComparison.OrdinalIgnoreCase))
   throw new Exception("Plik przygotowano dla innego adresu e-mail. Zaloguj się na właściwe konto.");
  string[] allowed=["import_rows","pay_rates","work_days","settlements","leave_ledger"];
  if(tables.Any(t=>!allowed.Contains(t.Key))) throw new Exception("Plik zawiera nieobsługiwany rodzaj danych.");
  // Validate the whole package before sending the first batch. The database enforces
  // row ownership and field constraints; imports never update existing records.
  foreach(var table in tables) {
   if(table.Value is not JsonArray rows) throw new Exception("Nieprawidłowy plik migracji.");
   var ids=new HashSet<Guid>();
   foreach(var item in rows) {
    if(item is not JsonObject row || !Guid.TryParse(row["id"]?.GetValue<string>(),out var id) || !ids.Add(id))
     throw new Exception("Brak identyfikatora lub powtórzony wpis w pliku migracji.");
    row["user_id"]=Session.User.Id.ToString();
    foreach(var key in new[]{"version","created_at","updated_at","deleted_at","total_net"}) row.Remove(key);
   }
  }
  int done=0,total=tables.Sum(t=>((JsonArray)t.Value!).Count);
  try {
   foreach(var table in allowed) {
    if(tables[table] is not JsonArray rows) continue;
    for(int offset=0;offset<rows.Count;offset+=100) {
     await Token();
     var batch=new JsonArray(rows.Skip(offset).Take(100).Select(r=>r!.DeepClone()).ToArray());
     using var req=Make(HttpMethod.Post,$"/rest/v1/mp_{table}?on_conflict=id",batch);
     req.Headers.Authorization=new("Bearer",Session!.AccessToken);
     req.Headers.Add("Prefer","resolution=ignore-duplicates,return=minimal");
     using var res=await http.SendAsync(req); await Ensure(res);
     done+=batch.Count;await progress($"Przenoszenie historii: {done} / {total} wpisów…");
    }
   }
   await Reload();
   if(LoadError is not null) throw new Exception(LoadError);
  } catch(Exception e) {
   throw new Exception($"Import zatrzymany po {done} / {total} wpisów. Zapisane partie pozostają na koncie. Możesz ponownie wybrać ten sam plik — nie nadpisze ani nie powieli zapisanych wpisów. {e.Message}");
  }
 }
 public static T Clone<T>(T row) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(row,Json),Json)!;
 void DemoSave<T>(T row) where T:Row {
  var saved=Clone(row); saved.Version++;
  if(saved is Settlement s)s.TotalNet=Payroll.Total(s);
  Replace(saved);
 }
 void Replace(Row row) {
  switch(row) {
   case WorkDay v: Put(Days,v);break;case ExtraWork v:Put(Extras,v);break;case PayRate v:Put(Rates,v);break;
   case Settlement v:Put(Settlements,v);break;case Payment v:Put(Payments,v);break;case LeaveEntry v:Put(Leaves,v);break;case Goal v:Put(Goals,v);break;
  }
 }
 static void Put<T>(List<T> list,T row) where T:Row {list.RemoveAll(x=>x.Id==row.Id); if(row.DeletedAt is null)list.Add(row);}
 public void StartDemo() {
  Demo=true; LoadError=null; Days=[];Extras=[];Rates=[];Settlements=[];Payments=[];Leaves=[];Goals=[];
  var today=DateOnly.FromDateTime(DateTime.Today);var month=new DateOnly(today.Year,today.Month,1);
  Rates.Add(new(){Version=1,EffectiveFrom=month.AddMonths(-12),BaseNet=7000,HourlyNet=65});
  for(int m=-5;m<0;m++){
   var s=new Settlement{Version=1,Month=month.AddMonths(m),Status="approved",ApprovedAt=DateTimeOffset.Now,ApprovedMinutes=900+(m+5)*180,BaseNetSnapshot=7000,HourlyNetSnapshot=65};
   s.TotalNet=Payroll.Total(s);Settlements.Add(s);Payments.Add(new(){Version=1,SettlementId=s.Id,PaidOn=s.Month.AddMonths(1).AddDays(9),AmountNet=s.TotalNet!.Value});
  }
  for(int d=1;d<=Math.Min(today.Day,DateTime.DaysInMonth(today.Year,today.Month));d++) {
   var date=month.AddDays(d-1); if(date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)continue;
   Days.Add(new(){Version=1,WorkDate=date,StartTime=new(7,0),EndTime=new(16,15),WorkedMinutes=555,Notes=d%2==0?"Praca nad projektem":"",Category="Biuro"});
  }
  Extras.Add(new(){Version=1,WorkDate=today,Minutes=120,ProjectName="Projekt dodatkowy",Notes="Dane przykładowe"});
  Leaves.Add(new(){Version=1,EffectiveDate=new(today.Year,1,1),EntitlementYear=today.Year,Kind="allocation",DaysDelta=26,Notes="Limit roczny"});
  Leaves.Add(new(){Version=1,EffectiveDate=today,EntitlementYear=today.Year,Kind="taken",DaysDelta=-8,Notes="Wypoczynek"});
  Goals.Add(new(){Version=1,Name="Dobry miesiąc",StartsOn=month,EndsOn=month.AddMonths(1).AddDays(-1),TargetNet=9500});
 }
}

