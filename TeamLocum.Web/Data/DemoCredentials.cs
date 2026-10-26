namespace TeamLocum.Web.Data;

/// <summary>
/// Published demo accounts for local development only. The options validator refuses them in any other environment.
/// All addresses use the reserved example.com domain and all GMC numbers are deliberately invalid placeholders.
/// </summary>
public static class DemoCredentials
{
    public const string AdminEmail = "admin@example.com";
    public const string AdminPassword = "Admin@Demo12345";

    public const string HospitalEmail = "city.hospital@example.com";
    public const string HospitalPassword = "Hospital@Demo12345";

    public const string DoctorEmail = "dr.sharma@example.com";
    public const string DoctorPassword = "Doctor@Demo12345";

    public static bool IsDemoPassword(string password) =>
        password is AdminPassword or HospitalPassword or DoctorPassword;
}
