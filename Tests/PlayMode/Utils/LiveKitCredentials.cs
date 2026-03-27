using System;

public struct LiveKitCredentials
{
    public string ServerUrl;
    public string ApiKey;
    public string ApiSecret;

    public static LiveKitCredentials CreateLocalDevCredentials()
    {
        return new LiveKitCredentials(){
            ServerUrl = "ws://localhost:7880",
            ApiKey = "devkey",
            ApiSecret = "secret"
        };
    }

    public static LiveKitCredentials CreateFromEnv()
    {
        return new LiveKitCredentials(){
            ServerUrl = ReadEnv("LK_TEST_URL", "ws://localhost:7880"),
            ApiKey = ReadEnv("LK_TEST_API_KEY", "devkey"),
            ApiSecret = ReadEnv("LK_TEST_API_SECRET", "secret")
        };
    }
    
    private static string ReadEnv(string key, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrEmpty(value))
            return defaultValue;
        return value.Trim();
    }
}