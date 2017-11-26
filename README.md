# VIEApps.Components.Caching.AspNet
The library helps ASP.NET Session State works with distributed cache (Memcached & Redis)
- Identities will have the "prefix" as the name of the provider (in web.config file)
- Using [VIEApps.Components.Caching](https://github.com/vieapps/Components.Caching) as main library
## NuGet
- Package ID: VIEApps.Components.Caching.AspNet
- Details: https://www.nuget.org/packages/VIEApps.Components.Caching.AspNet/
## Configuration for using Memcached
```xml
<sessionState mode="Custom" cookieless="UseCookies" cookieName=".ASPNET-Session-ID" regenerateExpiredSessionId="true" customProvider="MemcachedSessionStateProvider">
	<providers>
		<add name="MemcachedSessionStateProvider" type="net.vieapps.Components.Caching.AspNet.MemcachedSessionStateProvider, VIEApps.Components.Caching.AspNet" />
	</providers>
</sessionState>
```
Remarks: the name of the session state provider (MemcachedSessionStateProvider) will be used as prefix of all keys
## Configuration for using Redis
```xml
<sessionState mode="Custom" cookieless="UseCookies" cookieName=".ASPNET-Session-ID" regenerateExpiredSessionId="true" customProvider="RedisSessionStateProvider">
	<providers>
		<add name="RedisSessionStateProvider" type="net.vieapps.Components.Caching.AspNet.RedisSessionStateProvider, VIEApps.Components.Caching.AspNet" />
	</providers>
</sessionState>
```
Remarks: the name of the session state provider (RedisSessionStateProvider) will be used as prefix of all keys