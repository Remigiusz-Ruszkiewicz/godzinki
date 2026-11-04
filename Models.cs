using System.Text.Json.Serialization;
namespace Godzinki;
public class Row {
 public Guid Id {get;set;} = Guid.NewGuid();
 public Guid UserId {get;set;}
 public long Version {get;set;}
 public DateTimeOffset? DeletedAt {get;set;}
}
public class WorkDay : Row {
 public Guid? TripId {get;set;}
 public DateOnly WorkDate {get;set;} = DateOnly.FromDateTime(DateTime.Today);
 public TimeOnly? StartTime {get;set;}
 public TimeOnly? EndTime {get;set;}
 public bool EndsNextDay {get;set;}
 public int BreakMinutes {get;set;}
 public int? WorkedMinutes {get;set;}
 public int NormMinutes {get;set;} = 480;
 public int? OvertimeMinutesOverride {get;set;}
 public string DayType {get;set;} = "work";
 public string? Category {get;set;}
 public string? Notes {get;set;}
}
public class ExtraWork : Row {
 public DateOnly WorkDate {get;set;} = DateOnly.FromDateTime(DateTime.Today);
 public int Minutes {get;set;} = 60;
 public string? ProjectName {get;set;}
 public string? Category {get;set;}
 public string? Notes {get;set;}
}
public class PayRate : Row {
 public DateOnly EffectiveFrom {get;set;} = new(DateTime.Today.Year,DateTime.Today.Month,1);
 public decimal BaseNet {get;set;}
 public decimal HourlyNet {get;set;}
 public string Currency {get;set;} = "PLN";
 public string? Notes {get;set;}
}
public class Settlement : Row {
 public DateOnly Month {get;set;} = new(DateTime.Today.Year,DateTime.Today.Month,1);
 public string Status {get;set;} = "draft";
 public int? CompanyMinutesSnapshot {get;set;}
 public int? ExtraMinutesSnapshot {get;set;}
 public int? ApprovedMinutes {get;set;}
 public decimal? BaseNetSnapshot {get;set;}
 public decimal? HourlyNetSnapshot {get;set;}
 public decimal BonusNet {get;set;}
 public decimal CorrectionNet {get;set;}
 public decimal? TotalNet {get;set;}
 public string CalculationBasis {get;set;} = "separate_entries";
 public string? CorrectionReason {get;set;}
 public string? Notes {get;set;}
 public DateTimeOffset? ApprovedAt {get;set;}
}
public class Payment : Row {
 public Guid SettlementId {get;set;}
 public DateOnly PaidOn {get;set;} = DateOnly.FromDateTime(DateTime.Today);
 public decimal AmountNet {get;set;}
 public string? Notes {get;set;}
}
public class LeaveEntry : Row {
 public DateOnly EffectiveDate {get;set;} = DateOnly.FromDateTime(DateTime.Today);
 public int EntitlementYear {get;set;} = DateTime.Today.Year;
 public string Kind {get;set;} = "taken";
 public decimal DaysDelta {get;set;} = -1;
 public string? Notes {get;set;}
}
public class Goal : Row {
 public string Name {get;set;} = "Mój cel";
 public DateOnly StartsOn {get;set;} = new(DateTime.Today.Year,DateTime.Today.Month,1);
 public DateOnly EndsOn {get;set;} = new DateOnly(DateTime.Today.Year,DateTime.Today.Month,1).AddMonths(1).AddDays(-1);
 public decimal TargetNet {get;set;} = 10000;
 public string Basis {get;set;} = "earned";
}
public class Bonus : Row {
 public DateOnly Month {get;set;}=new(DateTime.Today.Year,DateTime.Today.Month,1);
 public string Name {get;set;}="Premia";
 public decimal AmountNet {get;set;}
 public string? Notes {get;set;}
}
public class Trip : Row {
 public string Destination {get;set;}="";
 public string Country {get;set;}="Polska";
 public string? Purpose {get;set;}
 public string Transport {get;set;}="Samochód";
 public DateOnly StartDate {get;set;}=DateOnly.FromDateTime(DateTime.Today);
 public TimeOnly StartTime {get;set;}=new(8,0);
 public DateOnly EndDate {get;set;}=DateOnly.FromDateTime(DateTime.Today);
 public TimeOnly EndTime {get;set;}=new(16,0);
 public decimal DailyRate {get;set;}
 public decimal AllowanceAmount {get;set;}
 public string Currency {get;set;}="PLN";
 public string? Notes {get;set;}
}
public class UserInfo { public Guid Id {get;set;} public string Email {get;set;} = ""; }
public class Session {
 public string AccessToken {get;set;} = "";
 public string RefreshToken {get;set;} = "";
 public long ExpiresIn {get;set;}
 public long ExpiresAt {get;set;}
 public UserInfo User {get;set;} = new();
}
public static class Payroll {
 public static int? ActualMinutes(TimeOnly? start, TimeOnly? end, bool nextDay, int pause) {
  if (start is null || end is null) return null;
  var minutes = (int)(end.Value.ToTimeSpan()-start.Value.ToTimeSpan()).TotalMinutes + (nextDay?1440:0) - pause;
  if (minutes < 0 || minutes > 2880) throw new InvalidOperationException("Sprawdź godziny, przerwę i zaznaczenie następnego dnia.");
  return minutes;
 }
 public static int Overtime(WorkDay w) => w.OvertimeMinutesOverride ?? (w.WorkedMinutes is int m ? m-w.NormMinutes : 0);
 public static decimal? Total(Settlement s) => s.ApprovedMinutes is int m && s.BaseNetSnapshot is decimal b && s.HourlyNetSnapshot is decimal r
  ? decimal.Round(b + m/60m*r + s.BonusNet + s.CorrectionNet,2,MidpointRounding.AwayFromZero) : null;
 public static string Duration(int minutes) => $"{(minutes<0?"−":"")}{Math.Abs(minutes)/60} h {Math.Abs(minutes)%60:00} min";
}

