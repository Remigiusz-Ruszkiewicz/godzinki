using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Globalization;
using System.Text.Json;
namespace Godzinki;
public partial class App {
 string page="Pulpit", modal="", email="",password="",authMode="login",notice="",error="";
 bool initialized,busy; DateOnly month=new(DateTime.Today.Year,DateTime.Today.Month,1);
 WorkDay work=new();ExtraWork extra=new();PayRate rate=new();Settlement settlement=new();Payment payment=new();LeaveEntry leave=new();Goal goal=new();
 string start="",end="";int? manualMinutes; Row? deleting;string deleteTable="";
 readonly string[] pages=["Pulpit","Kalendarz","Rozliczenia","Urlopy","Cele","Ustawienia"];
 IEnumerable<WorkDay> MonthDays=>Store.Days.Where(x=>SameMonth(x.WorkDate)).OrderBy(x=>x.WorkDate);
 IEnumerable<ExtraWork> MonthExtras=>Store.Extras.Where(x=>SameMonth(x.WorkDate));
 Settlement? Current=>Store.Settlements.FirstOrDefault(x=>x.Month==month);
 PayRate? CurrentRate=>Store.Rates.Where(x=>x.EffectiveFrom<=month).OrderByDescending(x=>x.EffectiveFrom).FirstOrDefault();
 bool RateChanges=>Store.Rates.Any(x=>x.EffectiveFrom>month && x.EffectiveFrom<month.AddMonths(1));
 int CompanyMinutes=>MonthDays.Sum(Payroll.Overtime);
 int ExtraMinutes=>MonthExtras.Sum(x=>x.Minutes);
 int TotalMinutes=>MonthDays.Sum(x=>x.WorkedMinutes??0)+ExtraMinutes;
 decimal? Estimate=>Current?.TotalNet ?? (CurrentRate is null?null:CurrentRate.BaseNet+Math.Max(0,CompanyMinutes+ExtraMinutes)/60m*CurrentRate.HourlyNet);
 decimal YearNet=>Store.Settlements.Where(x=>x.Month.Year==month.Year&&x.Status=="approved").Sum(x=>x.TotalNet??0);
 decimal PaidForMonth=>Current is null?0:Store.Payments.Where(x=>x.SettlementId==Current.Id).Sum(x=>x.AmountNet);
 decimal LeaveBalance=>Store.Leaves.Where(x=>x.EntitlementYear==month.Year).Sum(x=>x.DaysDelta);
 string MonthName=>month.ToString("MMMM yyyy",CultureInfo.GetCultureInfo("pl-PL"));
 bool SameMonth(DateOnly d)=>d.Year==month.Year&&d.Month==month.Month;
 string Money(decimal? amount)=>amount is null?"—":amount.Value.ToString("N2")+" zł";
 string Duration(int m)=>Payroll.Duration(m);
 string DayLabel(DateOnly d)=>d.ToString("dd MMM",CultureInfo.GetCultureInfo("pl-PL"));
 string Kind(string k)=>k switch {"taken"=>"Wykorzystanie","allocation"=>"Przydział","carryover"=>"Przeniesienie","correction"=>"Korekta",_=>k};
 string DayType(string k)=>k switch {"work"=>"Praca","leave"=>"Urlop","off"=>"Wolne","holiday"=>"Święto","sick"=>"Chorobowe","business_trip"=>"Delegacja",_=>k};
 string Initials=>Store.Demo?"DE":(Store.Session?.User.Email.FirstOrDefault().ToString().ToUpperInvariant()??"JA");
 protected override async Task OnAfterRenderAsync(bool first) {
  if(first){await Act(Store.Initialize);initialized=true;StateHasChanged();}
 }
 async Task Act(Func<Task> action,string success="") {
  if(busy)return;busy=true;error="";notice="";
  try{await action();notice=success;}catch(Exception e){error=e is HttpRequestException?"Nie udało się połączyć. Sprawdź internet i spróbuj ponownie.":e.Message;}
  finally{busy=false;StateHasChanged();}
 }
 async Task Auth() {
  if(authMode=="register") {
   if(password.Length<8){error="Hasło musi mieć przynajmniej 8 znaków.";return;}
   await Act(()=>Store.Register(email.Trim(),password,Nav.BaseUri),"Sprawdź pocztę i potwierdź adres, a następnie zaloguj się. Jeśli konto już istnieje, użyj logowania.");
  } else await Act(()=>Store.Login(email.Trim(),password));
  password="";
 }
 void Demo(){Store.StartDemo();page="Pulpit";error="";notice=""; }
 async Task Logout(){await Act(Store.Logout);page="Pulpit";modal="";password="";}
 void MoveMonth(int offset){month=month.AddMonths(offset);error="";notice="";}
 void ChangePage(string p){page=p;error="";notice="";}
 void OpenWork(WorkDay? existing=null,DateOnly? date=null) {
  work=existing is null?new(){WorkDate=date??(SameMonth(DateOnly.FromDateTime(DateTime.Today))?DateOnly.FromDateTime(DateTime.Today):month)}:DataStore.Clone(existing);
  start=work.StartTime?.ToString("HH:mm")??"";end=work.EndTime?.ToString("HH:mm")??"";
  manualMinutes=work.WorkedMinutes;modal="work";error="";
 }
 void OpenExtra(ExtraWork? existing=null){extra=existing is null?new(){WorkDate=SameMonth(DateOnly.FromDateTime(DateTime.Today))?DateOnly.FromDateTime(DateTime.Today):month}:DataStore.Clone(existing);modal="extra";error="";}
 void OpenRate(PayRate? existing=null){rate=existing is null?new(){EffectiveFrom=month}:DataStore.Clone(existing);modal="rate";error="";}
 void OpenLeave(LeaveEntry? existing=null){leave=existing is null?new(){EntitlementYear=month.Year}:DataStore.Clone(existing);modal="leave";error="";}
 void OpenGoal(Goal? existing=null){goal=existing is null?new(){StartsOn=month,EndsOn=month.AddMonths(1).AddDays(-1)}:DataStore.Clone(existing);modal="goal";error="";}
 void OpenSettlement() {
  settlement=Current is null?new(){Month=month,CompanyMinutesSnapshot=CompanyMinutes,ExtraMinutesSnapshot=ExtraMinutes,ApprovedMinutes=Math.Max(0,CompanyMinutes+ExtraMinutes),BaseNetSnapshot=CurrentRate?.BaseNet,HourlyNetSnapshot=CurrentRate?.HourlyNet}:DataStore.Clone(Current);
  modal="settlement";error="";
 }
 void RefreshProposal(){settlement.CompanyMinutesSnapshot=CompanyMinutes;settlement.ExtraMinutesSnapshot=ExtraMinutes;settlement.ApprovedMinutes=Math.Max(0,CompanyMinutes+ExtraMinutes);}
 void OpenPayment(){if(Current is null)return;payment=new(){SettlementId=Current.Id,AmountNet=Math.Max(0,(Current.TotalNet??0)-PaidForMonth)};modal="payment";error="";}
 void AskDelete(Row row,string table){deleting=row;deleteTable=table;modal="delete";error="";}
 async Task ConfirmDelete()=>await Act(async()=>{
  switch(deleting){
   case WorkDay r:await Store.Delete(deleteTable,r);break;case ExtraWork r:await Store.Delete(deleteTable,r);break;
   case PayRate r:await Store.Delete(deleteTable,r);break;case LeaveEntry r:await Store.Delete(deleteTable,r);break;
   case Goal r:await Store.Delete(deleteTable,r);break;case Payment r:await Store.Delete(deleteTable,r);break;
  } modal="";deleting=null;
 },"Wpis usunięty z zestawienia.");
 async Task Save()=>await Act(async()=>{
  switch(modal){
   case "work":
    work.StartTime=TimeOnly.TryParse(start,out var s)?s:null;work.EndTime=TimeOnly.TryParse(end,out var e)?e:null;
    if((work.StartTime is null)!=(work.EndTime is null))throw new Exception("Podaj zarówno początek, jak i koniec pracy, albo wpisz tylko czas w minutach.");
    if(work.DayType is "leave" or "off" or "holiday" or "sick"){work.NormMinutes=0;work.WorkedMinutes=0;work.OvertimeMinutesOverride=null;}
    else work.WorkedMinutes=Payroll.ActualMinutes(work.StartTime,work.EndTime,work.EndsNextDay,work.BreakMinutes)??manualMinutes;
    if(work.WorkedMinutes is null && work.OvertimeMinutesOverride is null && work.DayType is "work" or "business_trip")throw new Exception("Podaj godziny, czas pracy albo ręczne saldo nadgodzin.");
    if(Store.Days.Any(x=>x.WorkDate==work.WorkDate&&x.Id!=work.Id))throw new Exception("Ten dzień ma już wpis. Edytuj go w kalendarzu.");
    await Store.Save("work_days",work);break;
   case "extra":
    if(extra.Minutes<=0)throw new Exception("Czas dodatkowej pracy musi być większy od zera.");
    await Store.Save("extra_work",extra);break;
   case "rate":
    if(rate.BaseNet<0||rate.HourlyNet<0)throw new Exception("Stawki nie mogą być ujemne.");
    await Store.Save("pay_rates",rate);break;
   case "settlement":
    if(settlement.ApprovedMinutes is null or <0 || settlement.BaseNetSnapshot is null or <0 || settlement.HourlyNetSnapshot is null or <0)throw new Exception("Uzupełnij czas do wypłaty, podstawę i stawkę netto.");
    var proposed=(settlement.CompanyMinutesSnapshot??0)+(settlement.ExtraMinutesSnapshot??0);
    if(settlement.ApprovedMinutes!=Math.Max(0,proposed)&&string.IsNullOrWhiteSpace(settlement.CorrectionReason))throw new Exception("Dodaj powód ręcznej korekty czasu.");
    settlement.Status="approved";settlement.ApprovedAt=DateTimeOffset.UtcNow;
    await Store.Save("settlements",settlement);break;
   case "payment":
    if(payment.AmountNet<=0)throw new Exception("Podaj dodatnią kwotę wypłaty.");
    await Store.Save("payments",payment);break;
   case "leave":
    if(leave.DaysDelta==0 || leave.Kind=="taken"&&leave.DaysDelta>0 || (leave.Kind=="allocation"||leave.Kind=="carryover")&&leave.DaysDelta<0)throw new Exception("Urlop wykorzystany wpisz ze znakiem minus, a przydział i przeniesienie ze znakiem plus.");
    await Store.Save("leave_ledger",leave);break;
   case "goal":
    if(goal.TargetNet<=0 || goal.EndsOn<goal.StartsOn || string.IsNullOrWhiteSpace(goal.Name))throw new Exception("Sprawdź nazwę, dodatnią kwotę i zakres dat celu.");
    await Store.Save("goals",goal);break;
  }modal="";
 },"Zapisano.");
 decimal GoalValue(Goal g)=>g.Basis=="paid"?Store.Payments.Where(x=>x.PaidOn>=g.StartsOn&&x.PaidOn<=g.EndsOn).Sum(x=>x.AmountNet):Store.Settlements.Where(x=>x.Status=="approved"&&x.Month>=g.StartsOn&&x.Month<=g.EndsOn).Sum(x=>x.TotalNet??0);
 string Percent(decimal value,decimal max)=>Math.Clamp(max>0?value/max*100:0,0,100).ToString("0.##",CultureInfo.InvariantCulture)+"%";
 IEnumerable<(DateOnly Month,decimal Value)> History=>Enumerable.Range(-5,6).Select(i=>{var m=month.AddMonths(i);return(m,Store.Settlements.Where(x=>x.Month==m&&x.Status=="approved").Sum(x=>x.TotalNet??0));});
 decimal ChartMax=>Math.Max(1,History.Max(x=>x.Value));
 async Task Export()=>await Act(async()=>{
  var snapshot=new{format="godzinki-backup-v1",exportedAt=DateTimeOffset.UtcNow,days=Store.Days,extraWork=Store.Extras,rates=Store.Rates,settlements=Store.Settlements,payments=Store.Payments,leave=Store.Leaves,goals=Store.Goals};
  await JS.InvokeVoidAsync("godzinki.download",$"godzinki-{DateTime.Today:yyyy-MM-dd}.json",JsonSerializer.Serialize(snapshot,new JsonSerializerOptions(DataStore.Json){WriteIndented=true}),"application/json");
 },"Pobrano kopię danych.");
 async Task ExportCsv()=>await Act(async()=>{
  string Quote(string? v)=>"\""+(v??"").Replace("\"","\"\"")+"\"";
  var csv="Data;Rodzaj;Czas pracy (min);Saldo (min);Projekt;Notatki\r\n"+
    string.Join("\r\n",MonthDays.Select(x=>$"{x.WorkDate:yyyy-MM-dd};Praca;{x.WorkedMinutes};{Payroll.Overtime(x)};{Quote(x.Category)};{Quote(x.Notes)}")
    .Concat(MonthExtras.Select(x=>$"{x.WorkDate:yyyy-MM-dd};Dodatkowa;{x.Minutes};;{Quote(x.ProjectName)};{Quote(x.Notes)}")));
  await JS.InvokeVoidAsync("godzinki.download",$"godzinki-{month:yyyy-MM}.csv","\uFEFF"+csv,"text/csv;charset=utf-8");
 },"Pobrano zestawienie CSV.");
}

