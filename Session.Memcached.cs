#region Related components
using System;
using System.IO;
using System.Web;
using System.Web.UI;
using System.Web.SessionState;
using System.Collections.Specialized;

using Enyim.Caching.Memcached;
#endregion

namespace net.vieapps.Components.Caching.AspNet
{
	public class MemcachedSessionStateProvider : SessionStateStoreProviderBase
	{
		internal static Tuple<string, string> Prefixs = new Tuple<string, string>(null, null);

		public override void Initialize(string name, NameValueCollection config)
		{
			if (config == null)
				throw new ArgumentNullException(nameof(config), "No configuration is found");

			base.Initialize(name, config);
			MemcachedSessionStateProvider.Prefixs = new Tuple<string, string>("Header@" + name + "#", "Data@" + name + "#");
		}

		public override void InitializeRequest(HttpContext context) { }

		public override void EndRequest(HttpContext context) { }

		public override void Dispose() { }

		public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback) { return false; }

		public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
		{
			return new SessionStateStoreData(new SessionStateItemCollection(), SessionStateUtility.GetSessionStaticObjects(context), timeout);
		}

		public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
		{
			new MemcachedSessionStateItem()
			{
				Data = new SessionStateItemCollection(),
				Flag = SessionStateActions.InitializeItem,
				LockId = 0,
				Timeout = timeout
			}.Save(id, false, false);
		}

		public override SessionStateStoreData GetItem(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
		{
			var data = this.Get(context, false, id, out locked, out lockAge, out lockId, out actions);
			return (data == null)
				? null
				: data.ToStoreData(context);
		}

		public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
		{
			var data = this.Get(context, true, id, out locked, out lockAge, out lockId, out actions);
			return data == null
				? null
				: data.ToStoreData(context);
		}

		MemcachedSessionStateItem Get(HttpContext context, bool acquireLock, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
		{
			locked = false;
			lockId = null;
			lockAge = TimeSpan.Zero;
			actions = SessionStateActions.None;

			var data = MemcachedSessionStateItem.Load(id, false);
			if (data == null)
				return null;

			if (acquireLock)
			{
				// repeat until we can update the retrieved  item (i.e. nobody changes it between the time we get it from the store and updates its attributes)
				// Save() will return false if Cas() fails
				while (true)
				{
					if (data.LockId > 0)
						break;

					actions = data.Flag;
					data.LockId = data.HeadCas;
					data.LockTime = DateTime.UtcNow;
					data.Flag = SessionStateActions.None;

					// try to update the item in the store
					if (data.Save(id, true, true))
					{
						locked = true;
						lockId = data.LockId;
						return data;
					}

					// it has been modifed between we loaded and tried to save it
					data = MemcachedSessionStateItem.Load(id, false);
					if (data == null)
						return null;
				}
			}

			locked = true;
			lockAge = DateTime.UtcNow - data.LockTime;
			lockId = data.LockId;
			actions = SessionStateActions.None;

			return acquireLock
				? null
				: data;
		}

		public override void ReleaseItemExclusive(HttpContext context, string id, object lockId)
		{
			if (!(lockId is ulong))
				return;

			var tmp = (ulong)lockId;
			var data = MemcachedSessionStateItem.Load(id, true);

			if (data != null && data.LockId == tmp)
			{
				data.LockId = 0;
				data.LockTime = DateTime.MinValue;
				data.Save(id, true, true);
			}
		}

		public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
		{
			if (!(lockId is ulong))
				return;

			var tmp = (ulong)lockId;
			var data = MemcachedSessionStateItem.Load(id, true);

			if (data != null && data.LockId == tmp)
				MemcachedSessionStateItem.Remove(id);
		}

		public override void ResetItemTimeout(HttpContext context, string id)
		{
			var data = MemcachedSessionStateItem.Load(id, false);
			if (data != null)
				data.Save(id, false, true);
		}

		public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
		{
			MemcachedSessionStateItem data = null;
			var existing = false;

			if (!newItem)
			{
				if (!(lockId is ulong))
					return;

				var tmp = (ulong)lockId;
				data = MemcachedSessionStateItem.Load(id, true);
				existing = data != null;

				// if we're expecting an existing item, but it's not in the cache or it's not locked or it's locked by someone else, then quit
				if (!newItem && (!existing || data.LockId == 0 || data.LockId != tmp))
					return;
			}

			if (!existing)
				data = new MemcachedSessionStateItem();

			// set the new data and reset the locks
			data.Timeout = item.Timeout;
			data.Data = (SessionStateItemCollection)item.Items;
			data.Flag = SessionStateActions.None;
			data.LockId = 0;
			data.LockTime = DateTime.MinValue;

			data.Save(id, false, existing && !newItem);
		}
	}

	// -----------------------------------------------------

	internal class MemcachedSessionStateItem
	{
		public SessionStateItemCollection Data;
		public SessionStateActions Flag;
		public ulong LockId;
		public DateTime LockTime;

		public int Timeout;                // in minutes

		public ulong HeadCas;
		public ulong DataCas;

		void SaveHeader(MemoryStream stream)
		{
			var pair = new Pair((byte)1, new Triplet((byte)this.Flag, this.Timeout, new Pair(this.LockId, this.LockTime.ToBinary())));
			new ObjectStateFormatter().Serialize(stream, pair);
		}

		public bool Save(string id, bool metaOnly, bool useCas)
		{
			using (var stream = new MemoryStream())
			{
				this.SaveHeader(stream);
				var timespan = TimeSpan.FromMinutes(this.Timeout);
				var key = MemcachedSessionStateProvider.Prefixs.Item1 + id;
				var value = new ArraySegment<byte>(stream.GetBuffer(), 0, (int)stream.Length);
				bool result = useCas
					? Memcached.Client.Cas(StoreMode.Set, key, value, timespan, this.HeadCas).Result
					: Memcached.Client.Store(StoreMode.Set, key, value, timespan);

				if (!metaOnly)
				{
					stream.Position = 0;
					using (var writer = new BinaryWriter(stream))
					{
						this.Data.Serialize(writer);
						key = MemcachedSessionStateProvider.Prefixs.Item2 + id;
						value = new ArraySegment<byte>(stream.GetBuffer(), 0, (int)stream.Length);

						result = useCas
							? Memcached.Client.Cas(StoreMode.Set, key, value, timespan, this.DataCas).Result
							: Memcached.Client.Store(StoreMode.Set, key, value, timespan);
					}
				}

				return result;
			}
		}

		static MemcachedSessionStateItem LoadItem(MemoryStream stream)
		{
			var graph = new ObjectStateFormatter().Deserialize(stream) as Pair;
			if (graph == null)
				return null;

			if (((byte)graph.First) != 1)
				return null;

			var trip = (Triplet)graph.Second;
			var result = new MemcachedSessionStateItem();
			result.Flag = (SessionStateActions)(byte)trip.First;
			result.Timeout = (int)trip.Second;
			var lockInfo = (Pair)trip.Third;
			result.LockId = (ulong)lockInfo.First;
			result.LockTime = DateTime.FromBinary((long)lockInfo.Second);

			return result;
		}

		public static MemcachedSessionStateItem Load(string id, bool metaOnly)
		{
			var header = Memcached.Client.GetWithCas<byte[]>(MemcachedSessionStateProvider.Prefixs.Item1 + id);
			if (header.Result == null)
				return null;

			MemcachedSessionStateItem entry;
			using (var stream = new MemoryStream(header.Result))
			{
				entry = MemcachedSessionStateItem.LoadItem(stream);
			}

			if (entry != null)
				entry.HeadCas = header.Cas;

			if (metaOnly)
				return entry;

			var data = Memcached.Client.GetWithCas<byte[]>(MemcachedSessionStateProvider.Prefixs.Item2 + id);
			if (data.Result == null)
				return null;

			using (var stream = new MemoryStream(data.Result))
			{
				using (var reader = new BinaryReader(stream))
				{
					entry.Data = SessionStateItemCollection.Deserialize(reader);
				}
			}

			entry.DataCas = data.Cas;
			return entry;
		}

		public SessionStateStoreData ToStoreData(HttpContext context)
		{
			return new SessionStateStoreData(this.Data, SessionStateUtility.GetSessionStaticObjects(context), this.Timeout);
		}

		public static void Remove(string id)
		{
			Memcached.Client.Remove(MemcachedSessionStateProvider.Prefixs.Item1 + id);
			Memcached.Client.Remove(MemcachedSessionStateProvider.Prefixs.Item2 + id);
		}
	}

}