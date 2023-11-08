using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable.Routing
{


    internal class RouteTable
    {
        private class Routes
        {
            private object _lock = new object();

            public LinkedList<Route> Values { get; } = new LinkedList<Route>();

            public void Add(PeerId peer, NetworkConnection? connection, int hops)
            {
                lock (_lock)
                {
                    var current = Values.First;
                    while (current != null && current.ValueRef.Hops < hops)
                    {
                        current = current.Next;
                    }

                    if (current != null)
                    {
                        Values.AddBefore(current, new Route(peer, connection, hops));
                    }
                    else
                    {
                        Values.AddLast(new Route(peer, connection, hops));
                    }
                }
            }


            public bool TryGetRoute([NotNullWhen(true)] out Route? route)
            {
                if (Values.Count > 0)
                {
                    lock (_lock)
                    {
                        var current = Values.First;
                        while (current != null && current.ValueRef.Connection != null && !current.ValueRef.Connection.TryGetTarget(out _))
                        {
                            Values.RemoveFirst();
                            current = Values.First;
                        }

                        if (current != null)
                        {
                            route = current.Value;
                            return true;
                        }
                        else
                        {
                            route = null;
                            return false;
                        }
                    }
                }
                else
                {
                    route = null;
                    return false;
                }
            }

            public void Remove(NetworkConnection connection)
            {
                if (Values.Count > 0)
                {
                    lock (_lock)
                    {
                        var current = Values.First;
                        while (current != null)
                        {
                            if (current.ValueRef.Connection != null && (!current.ValueRef.Connection.TryGetTarget(out var c) || c == connection))
                            {
                                var old = current;
                                current = current.Next;
                                Values.Remove(old);

                            }
                            else
                            {
                                current = current.Next;
                            }
                        }
                    }
                }
            }
        }

        private ConcurrentDictionary<PeerId, Routes> _routes = new ConcurrentDictionary<PeerId, Routes>();

        public RouteTable(PeerId localPeer)
        {
            //Add loopback route.
            AddRoute(localPeer, null, 0);
        }

        public bool TryGetRoute(PeerId destination, [NotNullWhen(true)] out Route? route)
        {

            if (_routes.TryGetValue(destination, out var routes))
            {
                if (routes.TryGetRoute(out route))
                {
                    return true;
                }
                else
                {
                    Debugger.Log(0, "test", $"no route to {destination} found. Existing entries {string.Join(",", routes.Values.Select(k => k.Peer.ToString()))}");
                    return false;
                }
            }
            else
            {
                
                Debugger.Log(0, "test", $"Route entries to {destination} not found. Existing entries {string.Join(",", _routes.Keys.Select(k => k.ToString()))}");
                route = null;
                return false;
            }
        }


        public void AddRoute(PeerId peer, NetworkConnection? connection, int hops)
        {
            var routes = _routes.GetOrAdd(peer, _ => new Routes());

            routes.Add(peer, connection, hops);
        }

        public void RemoveRoutes(NetworkConnection connection)
        {
            foreach (var routes in _routes.Values)
            {
                routes.Remove(connection);
            }
        }
    }
}
