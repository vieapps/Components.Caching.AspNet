#region Related components
using System;
using System.IO;
using System.Web;
using System.Web.SessionState;
using System.Configuration;
using System.Collections.Specialized;
using System.Runtime.Serialization.Formatters.Binary;
#endregion

namespace net.vieapps.Components.Caching.AspNet
{
	public class RedisSessionStateProvider : SessionStateStoreProviderBase
	{
		internal static string Prefix = null;

		public override void Initialize(string name, NameValueCollection config)
		{
			if (config == null)
				throw new ArgumentNullException(nameof(config), "No configuration is found");

			base.Initialize(name, config);
			RedisSessionStateProvider.Prefix = name + "@";
		}

		public override void InitializeRequest(HttpContext context) { }

		public override void EndRequest(HttpContext context) { }

		public override void Dispose() { }

		public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback) { return false; }

		public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
		{
			return this.GetSessionStoreItem(context, id, out locked, out lockAge, out lockId, out actions);
		}

		public override SessionStateStoreData GetItem(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
		{
			return this.GetSessionStoreItem(context, id, out locked, out lockAge, out lockId, out actions);
		}

		private SessionStateStoreData GetSessionStoreItem(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
		{
			// there is no locking of any session data
			locked = false; 
			lockAge = TimeSpan.Zero;
			lockId = DateTime.UtcNow.Ticks;
			actions = SessionStateActions.None;

			var data = Redis.Client.Get<RedisSessionStateItem>(RedisSessionStateProvider.Prefix + id);
			if (data == null)
				return null;

			var sessionData = data.Deserialize(context);
			actions = data.GetActionFlag();
			return sessionData;
		}

		public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
		{
			var data = new RedisSessionStateItem(SessionStateActions.None, item.Timeout);
			data.Serialize((SessionStateItemCollection)item.Items);
			Redis.Client.Set(RedisSessionStateProvider.Prefix + id, data, item.Timeout);
		}

		public override void ReleaseItemExclusive(HttpContext context, string id, object lockId) { }

		public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
		{
			Redis.Client.Remove(RedisSessionStateProvider.Prefix + id);
		}

		public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
		{
			Redis.Client.Set(RedisSessionStateProvider.Prefix + id, new RedisSessionStateItem(SessionStateActions.InitializeItem, timeout), TimeSpan.FromMinutes(timeout));
		}

		public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
		{
			return new SessionStateStoreData(new SessionStateItemCollection(), SessionStateUtility.GetSessionStaticObjects(context), timeout);
		}

		public override void ResetItemTimeout(HttpContext context, string id)
		{
			var data = Redis.Client.Get<RedisSessionStateItem>(RedisSessionStateProvider.Prefix + id);
			if (data != null)
				Redis.Client.Set(RedisSessionStateProvider.Prefix + id, data, data.Timeout);
		}
	}

	// -----------------------------------------------------

	[Serializable]
	internal class RedisSessionStateItem
	{
		int _actionFlag;
		int _timeout;
		byte[] _serializedSessionData;

		public int Timeout
		{
			get { return this._timeout; }
		}

		public RedisSessionStateItem() { }

		public RedisSessionStateItem(SessionStateActions actionFlag, int timeout)
		{
			this._actionFlag = (int)actionFlag;
			this._timeout = timeout;
			this._serializedSessionData = null;
		}

		public void Serialize(SessionStateItemCollection items)
		{
			using (var stream = new MemoryStream())
			{
				using (var writer = new BinaryWriter(stream))
				{
					if (items != null)
						items.Serialize(writer);
				}
				this._serializedSessionData = stream.ToArray();
			}
		}

		public SessionStateStoreData Deserialize(HttpContext context)
		{
			using (var stream = this._serializedSessionData == null ? new MemoryStream() : new MemoryStream(this._serializedSessionData))
			{
				var items = new SessionStateItemCollection();
				if (stream.Length > 0)
					using (var reader = new BinaryReader(stream))
					{
						items = SessionStateItemCollection.Deserialize(reader);
					}
				return new SessionStateStoreData(items, SessionStateUtility.GetSessionStaticObjects(context), this.Timeout);
			}
		}

		public SessionStateActions GetActionFlag()
		{
			return (SessionStateActions)this._actionFlag;
		}
	}
}