using BlazorIdentity.Client;
using BlazorIdentity.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Claims;

namespace BlazorIdentity.Identity
{
	public class PersistingRevalidatingAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
	{
		private readonly IServiceScopeFactory _scopeFactory;
		private readonly PersistentComponentState _state;
		private readonly IdentityOptions _options;

		private readonly PersistingComponentStateSubscription _subscription;

		private Task<AuthenticationState>? _authenticationStateTask;

		public PersistingRevalidatingAuthenticationStateProvider(
			ILoggerFactory loggerFactory,
			IServiceScopeFactory scopeFactory,
			PersistentComponentState state,
			IOptions<IdentityOptions> options)
			: base(loggerFactory)
		{
			_scopeFactory = scopeFactory;
			_state = state;
			_options = options.Value;

			// Password settings
			_options.Password.RequireDigit = true;
			_options.Password.RequiredLength = 8;
			_options.Password.RequireNonAlphanumeric = false;
			_options.Password.RequireUppercase = true;
			_options.Password.RequireLowercase = false;
			
			// Lockout settings
			_options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
			_options.Lockout.MaxFailedAccessAttempts = 10;
			
			// // Cookie settings
			// _options.Cookies.ApplicationCookie.ExpireTimeSpan = TimeSpan.FromDays(150);
			// _options.Cookies.ApplicationCookie.LoginPath = "/Account/LogIn";
			// _options.Cookies.ApplicationCookie.LogoutPath = "/Account/LogOff";
			
			// // User settings
			// //_options.User.RequireUniqueEmail = true;

			AuthenticationStateChanged += OnAuthenticationStateChanged;
			_subscription = state.RegisterOnPersisting(OnPersistingAsync, RenderMode.InteractiveWebAssembly);
		}

		protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

		protected override async Task<bool> ValidateAuthenticationStateAsync(
			AuthenticationState authenticationState, CancellationToken cancellationToken)
		{
			// Get the user manager from a new scope to ensure it fetches fresh data
			await using var scope = _scopeFactory.CreateAsyncScope();
			var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
			return await ValidateSecurityStampAsync(userManager, authenticationState.User);
		}

		private async Task<bool> ValidateSecurityStampAsync(UserManager<ApplicationUser> userManager, ClaimsPrincipal principal)
		{
			var user = await userManager.GetUserAsync(principal);
			if (user == null)
			{
				return false;
			}
			else if (!userManager.SupportsUserSecurityStamp)
			{
				return true;
			}
			else
			{
				var principalStamp = principal.FindFirstValue(_options.ClaimsIdentity.SecurityStampClaimType);
				var userStamp = await userManager.GetSecurityStampAsync(user);
				return principalStamp == userStamp;
			}
		}

		private void OnAuthenticationStateChanged(Task<AuthenticationState> authenticationStateTask)
		{
			_authenticationStateTask = authenticationStateTask;
		}

		private async Task OnPersistingAsync()
		{
			if (_authenticationStateTask is null)
			{
				throw new UnreachableException($"Authentication state not set in {nameof(RevalidatingServerAuthenticationStateProvider)}.{nameof(OnPersistingAsync)}().");
			}

			var authenticationState = await _authenticationStateTask;
			var principal = authenticationState.User;

			if (principal.Identity?.IsAuthenticated == true)
			{
				var userId = principal.FindFirst(_options.ClaimsIdentity.UserIdClaimType)?.Value;
				var email = principal.FindFirst(_options.ClaimsIdentity.EmailClaimType)?.Value;

				if (userId != null && email != null)
				{
					_state.PersistAsJson(nameof(UserInfo), new UserInfo
					{
						UserId = userId,
						Email = email,
					});
				}
			}
		}

		protected override void Dispose(bool disposing)
		{
			_subscription.Dispose();
			AuthenticationStateChanged -= OnAuthenticationStateChanged;
			base.Dispose(disposing);
		}
	}
}
