using System;
using System.Reflection;
using Company.ChestGame.Minigame.Core;

namespace Company.ChestGame.Tests.Common
{
    // MinigameBaseSO.Id is a serialized field authored on the asset, so a definition built with
    // CreateInstance carries an empty one. Tests write the field directly, the same "reflect the
    // field in" pattern ChestElementViewLifetimeTests uses for a view's serialized references.
    // Keeping it here means no production type has to open a setter it does not otherwise need.
    public static class MinigameDefinitionAuthoring
    {
        private static readonly FieldInfo IdField =
            typeof(MinigameBaseSO).GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic);

        public static TDefinition WithId<TDefinition>(this TDefinition definition, string id)
            where TDefinition : MinigameBaseSO
        {
            // A rename of the field would otherwise surface as a NullReferenceException from a
            // helper nobody suspects, in every test that authors an id.
            if (IdField == null)
            {
                throw new MissingFieldException(
                    $"{nameof(MinigameBaseSO)} no longer has a '_id' field; MinigameDefinitionAuthoring needs updating");
            }

            IdField.SetValue(definition, id);
            return definition;
        }
    }
}
