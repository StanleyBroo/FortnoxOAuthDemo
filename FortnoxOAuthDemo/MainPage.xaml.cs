// FortnoxOAuthDemo - MainPage.xaml.cs med WebView-flöde (uppdaterad redirect URI)
using System.Text;
using Newtonsoft.Json;
using System.Web;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;


namespace FortnoxOAuthDemo
{

    public static class ConfigProvider{public static IConfiguration Configuration { get; set; }}
    public partial class MainPage : ContentPage
    {
        private string ClientId;
        private string ClientSecret;
        private string RedirectUri;
        private const string Scope = "customer price article invoice";

        private string YourHostDomain; // Replace with your domain i.e. www.yourdomain.com

        private WebView webView;

        public MainPage()
        {
            // FortnoxOAuthDemo - MainPage.xaml.cs
            // QUICK PROTOTYPE: Code only for testing purposes.
            // Remove in production. Split responsibilities between backend and mobile app.
            // Handle secret storage securely in a backend vault after proper user authentication in your LOB system.
            // Add proper error handling and logging.

            InitializeComponent();
            var config = new ConfigurationBuilder().AddUserSecrets<MainPage>().Build();
            ClientId = config["Fortnox:ClientId"];
            ClientSecret = config["Fortnox:ClientSecret"];
            RedirectUri = config["Fortnox:RedirectUri"];


            YourHostDomain =config["Fortnox:YourHostDomain"]; 

            webView = new WebView
            {
                HorizontalOptions = LayoutOptions.FillAndExpand,
                VerticalOptions = LayoutOptions.FillAndExpand
            };

            webView.Navigating += OnWebViewNavigating;

            var getTokenButton = new Button { Text = "Get Token", Margin = 5 };
            getTokenButton.Clicked += async (s, e) =>
            {
                var token = await GetValidAccessTokenAsync();
                if (token == null)
                {
                    LoginToFortnoxAndApproveIntegration();
                }
                else
                {
                    await DisplayAlert("Token allready exists", token, "OK");
                }
            };


            var refreshTokenButton = new Button { Text = "Refresh Token",Margin=5 };
            refreshTokenButton.Clicked += async (s, e) =>
            {
                var refreshToken = await SecureStorage.GetAsync("refresh_token");
                var newToken = await RefreshAccessTokenAsync(refreshToken);
                if (newToken == null)
                {
                    LoginToFortnoxAndApproveIntegration();
                }
                else
                {
                    await DisplayAlert("Token refreshed", newToken, "OK");
                }
            };


            var clearTokenButton = new Button { Text = "Remove Token" , Margin = 5 };
            clearTokenButton.Clicked += async (s, e) =>
            {
                SecureStorage.Remove("access_token");
                SecureStorage.Remove("refresh_token");
                SecureStorage.Remove("expires_at");
                await DisplayAlert("Token removed", "Token has been removed", "OK");
            };




            Content = new StackLayout
            {
                Padding = 20,
                Margin = 20,
                Children = { getTokenButton,refreshTokenButton,clearTokenButton, webView }
            };


        }

       

        private void LoginToFortnoxAndApproveIntegration()
        {
            var state = Guid.NewGuid().ToString();
            var authUrl = $"https://apps.fortnox.se/oauth-v1/auth?client_id={ClientId}&redirect_uri={RedirectUri}&response_type=code&scope={Scope}&access_type=offline&state={state}";
            webView.Source = authUrl;
        }

        private async void OnWebViewNavigating(object sender, WebNavigatingEventArgs e)
        {
            var uri = new Uri(e.Url);

            if (uri.Scheme == "https" && uri.Host == "www.ccthub.com")
            {
                e.Cancel = true;

                var code = HttpUtility.ParseQueryString(uri.Query).Get("code");
                if (!string.IsNullOrEmpty(code))
                {
                    await GetTokenAsync(code);
                }
            }
        }

        public async Task GetTokenAsync(string code)
        {
            var httpClient = new HttpClient();

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "redirect_uri", RedirectUri }
            });

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, "https://apps.fortnox.se/oauth-v1/token")
            {
                Content = content
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

            var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var token = JsonConvert.DeserializeObject<FortnoxTokenResponse>(json);

                await SecureStorage.SetAsync("access_token", token.AccessToken);
                await SecureStorage.SetAsync("refresh_token", token.RefreshToken);
                await SecureStorage.SetAsync("expires_at", DateTime.UtcNow.AddSeconds(token.ExpiresIn).ToString("o"));

                await DisplayAlert("Login", "Token mottagen och sparad!", "OK");
            }
            else
            {
                await DisplayAlert("Fel", $"Token hämtning misslyckades: {response.StatusCode}", "OK");
            }
        }

        public async Task<string> GetValidAccessTokenAsync()
        {
            var accessToken = await SecureStorage.GetAsync("access_token");
            var refreshToken = await SecureStorage.GetAsync("refresh_token");
            var expiresAtStr = await SecureStorage.GetAsync("expires_at");

            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(expiresAtStr))
            {
               
                return null; // Ingen token, måste logga in
            }

            if (!DateTime.TryParse(expiresAtStr, out var expiresAt))
                return null;

            if (DateTime.UtcNow.AddMinutes(2) >= expiresAt)
            {
                // Token har gått ut eller går ut snart – försök förnya
                var newToken = await RefreshAccessTokenAsync(refreshToken);
                return newToken; //might be null if refresh token has expired
            }

            return accessToken;
        }

        private async Task<string> RefreshAccessTokenAsync(string refreshToken)
        {
            var httpClient = new HttpClient();
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken }
            });

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, "https://apps.fortnox.se/oauth-v1/token")
            {
                Content = content
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var token = JsonConvert.DeserializeObject<FortnoxTokenResponse>(json);

            await SecureStorage.SetAsync("access_token", token.AccessToken);
            await SecureStorage.SetAsync("refresh_token", token.RefreshToken);
            await SecureStorage.SetAsync("expires_at", DateTime.UtcNow.AddSeconds(token.ExpiresIn).ToString("o"));

            return token.AccessToken;
        }
    }

    public class FortnoxTokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
