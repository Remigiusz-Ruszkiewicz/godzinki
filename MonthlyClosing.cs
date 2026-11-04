namespace Godzinki;

// A live projection of closed months, not a scheduled write. Saved settlements
// take precedence; new months use recorded time and effective rates on each read.
public static class MonthlyClosing {
 public static DateOnly Month(DateOnly d)=>new(d.Year,d.Month,1);
 public static Settlement? Propose(DateOnly month,List<WorkDay> days,List<ExtraWork> extras,List<PayRate> rates) {
  var end=month.AddMonths(1);
  var rate=rates.Where(x=>x.EffectiveFrom<=month).MaxBy(x=>x.EffectiveFrom);
  if(rate is null||rates.Any(x=>x.EffectiveFrom>month&&x.EffectiveFrom<end))return null;
  var work=days.Where(x=>x.WorkDate>=month&&x.WorkDate<end).ToList();
  // Missing time in an actual working day requires user review, not a zero assumption.
  if(work.Any(x=>x.DayType is "work" or "business_trip" && x.WorkedMinutes is null && x.OvertimeMinutesOverride is null))return null;
  var company=work.Sum(Payroll.Overtime);
  var extra=extras.Where(x=>x.WorkDate>=month&&x.WorkDate<end).Sum(x=>x.Minutes);
  var result=new Settlement{Month=month,Status="approved",CompanyMinutesSnapshot=company,ExtraMinutesSnapshot=extra,
   ApprovedMinutes=Math.Max(0,company+extra),BaseNetSnapshot=rate.BaseNet,HourlyNetSnapshot=rate.HourlyNet,
   CalculationBasis="separate_entries",Notes="Naliczono automatycznie z ewidencji i stawek. Zatwierdź, aby utrwalić kwotę."};
  result.TotalNet=Payroll.Total(result);return result;
 }
 public static List<Settlement> Report(DataStore store) {
  var cutoff=Month(store.Today);
  var results=store.Settlements.Where(x=>x.Month<cutoff&&Payroll.Total(x) is not null).Select(DataStore.Clone).ToList();
  var lastLegacy=store.Settlements.Where(x=>x.CalculationBasis=="legacy_excel").Select(x=>x.Month).DefaultIfEmpty(DateOnly.MinValue).Max();
  var months=store.Days.Where(x=>x.WorkedMinutes.HasValue||x.OvertimeMinutesOverride.HasValue).Select(x=>Month(x.WorkDate))
   .Concat(store.Extras.Select(x=>Month(x.WorkDate))).Concat(store.Bonuses.Select(x=>x.Month)).Distinct();
  foreach(var month in months.Where(x=>x<cutoff&&x>lastLegacy)) {
   if(store.Settlements.Any(x=>x.Month==month))continue;
   var proposed=Propose(month,store.Days,store.Extras,store.Rates);
   if(proposed is not null)results.Add(proposed);
  }
  // A recorded bonus must not disappear just because the salary/time needs review.
  foreach(var bonusMonth in store.Bonuses.Where(x=>x.Month<cutoff).Select(x=>x.Month).Distinct()) {
   if(results.Any(x=>x.Month==bonusMonth))continue;
   results.Add(new Settlement{Month=bonusMonth,Status="approved",ApprovedMinutes=0,BaseNetSnapshot=0,HourlyNetSnapshot=0,
    Notes="Tylko premie — rozliczenie pracy wymaga uzupełnienia."});
  }
  foreach(var result in results){result.Status="approved";result.BonusNet+=store.Bonuses.Where(x=>x.Month==result.Month).Sum(x=>x.AmountNet);result.TotalNet=Payroll.Total(result);}
  return results.OrderBy(x=>x.Month).ToList();
 }
 public static List<DateOnly> Pending(DataStore store,List<Settlement> report){
  var cutoff=Month(store.Today);
  var legacy=store.Settlements.Where(x=>x.CalculationBasis=="legacy_excel").Select(x=>x.Month).DefaultIfEmpty(DateOnly.MinValue).Max();
  return store.Days.Select(x=>Month(x.WorkDate)).Concat(store.Extras.Select(x=>Month(x.WorkDate)))
   .Concat(store.Bonuses.Select(x=>x.Month)).Concat(store.Settlements.Where(x=>Payroll.Total(x) is null).Select(x=>x.Month))
   .Where(x=>x<cutoff&&x>legacy).Distinct()
   .Where(x=>!report.Any(r=>r.Month==x)||report.Any(r=>r.Month==x&&r.Notes=="Tylko premie — rozliczenie pracy wymaga uzupełnienia."))
   .OrderBy(x=>x).ToList();
 }
}
