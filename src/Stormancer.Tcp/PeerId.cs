using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Tcp
{
    /// <summary>
    /// Uniquely identifies a peer in the network.
    /// </summary>
    public readonly struct PeerId : IEquatable<PeerId>
    {
        /// <summary>
        /// Creates a new unique peer id.
        /// </summary>
        /// <returns></returns>
        static internal PeerId CreateNew()
        {
            return new PeerId(Guid.NewGuid());
        }

        /// <summary>
        /// Compares the object with another <see cref="PeerId"/>.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Equals(PeerId other)
        {
            return this.Value.Equals(other.Value);
        }

        /// <summary>
        /// Computes the hash code of the object.
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <summary>
        /// Compares the object for equality.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj == null) return false;

            if(obj is PeerId other)
            {
                return Equals(other);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Compares for equality.
        /// </summary>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        /// <returns></returns>
        public static bool operator==(PeerId v1, PeerId v2)
        {
            return v1.Equals(v2);
        }

        /// <summary>
        /// Compares for inequality.
        /// </summary>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        /// <returns></returns>
        public static bool operator !=(PeerId v1, PeerId v2)
        {
            return !v1.Equals(v2);
        }

        internal PeerId(Guid value)
        {
            Value = value;
        }

        /// <summary>
        /// Creates an empty peerId.
        /// </summary>
        public PeerId()
        {
            Value = Guid.Empty;
        }

        /// <summary>
        /// Gets the unique identifier in the peer id.
        /// </summary>
        public Guid Value { get; }
    }
}
