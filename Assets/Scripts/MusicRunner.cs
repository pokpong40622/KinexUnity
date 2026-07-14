using UnityEngine;

namespace Kinex
{
    /// <summary>
    /// Bare MonoBehaviour that exists only so Music (a static class) has something to run
    /// fade/duck coroutines on. No public API — Music owns all logic and just calls
    /// StartCoroutine/StopAllCoroutines on the instance it creates.
    /// </summary>
    internal class MusicRunner : MonoBehaviour
    {
    }
}
