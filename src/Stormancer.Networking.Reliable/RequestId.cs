using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.Networking.Reliable
{
    /// <summary>
    /// The id of a request.
    /// </summary>
    public readonly struct RequestId : IEquatable<RequestId>
    {
        internal RequestId(PeerId origin, int id)
        {
            Origin = origin;
            Id = id;
        }
        /// <summary>
        /// Gets the id of the request.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Gets the origin of the request.
        /// </summary>
        public PeerId Origin { get; }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Equals(RequestId other)
        {
            return Origin.Equals(other.Origin) && Id == other.Id;
        }


        /// <summary>
        /// Indicates whether the current object is equal to another object.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is RequestId other)
            {
                return Equals(other);
            }
            else
            {
                return false;
            }

        }

        /// <summary>
        /// Indicates whether two objects of this type are equal.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator ==(RequestId left, RequestId right)
        {
            return left.Equals(right);
        }


        /// <summary>
        ///  Indicates whether two objects of this type are different.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator !=(RequestId left, RequestId right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Creates an hash code for the <see cref="RequestId"/> object.
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Origin.GetHashCode(), Id);
        }
    }
}
