using UnityEngine;

namespace Kinex.FruitGame
{
    /// <summary>
    /// The only environment object saved in the scene — builds the whole orchard at runtime so
    /// no material/mesh assets are needed (see FruitStageBuilder).
    /// </summary>
    public class FruitStage : MonoBehaviour
    {
        void Awake() => FruitStageBuilder.BuildStage(transform);
    }
}
