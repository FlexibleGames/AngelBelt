using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace AngelBelt
{
    class AngelBeltItem : Item
    {
        public EnumCharacterDressType DressType { get; private set; }

        public bool IsArmor = false;                

        public bool UseCharge => Attributes != null ? base.Attributes["usecharge"].AsBool(false) : false;
        public int ChargePerSecond => Attributes != null ? base.Attributes["chargepersecond"].AsInt(1) : 1;

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
//            handling = EnumHandHandling.Handled;
            EntityPlayer entityPlayer = byEntity as EntityPlayer;
            IPlayer player = (entityPlayer != null) ? entityPlayer.Player : null;
            if (player == null)
                return;

            IInventory ownInventory = player.InventoryManager.GetOwnInventory("character");
            if (ownInventory == null)
                return;

            if (this.DressType == EnumCharacterDressType.Unknown)
                return;

            // must sneak + right click to toggle item
            if (!byEntity.Controls.Sneak)
            {
                // if not sneaking, try to place item into slot
                if (ownInventory[(int)this.DressType].TryFlipWith(slot))
                {
                    handling = EnumHandHandling.PreventDefault;
                }
                return;
            }
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            JsonObject attributes = itemstack.Collectible.Attributes;
            if (attributes == null)
                return;

            Dictionary<string, MultiTextureMeshRef> orCreate = ObjectCacheUtil.GetOrCreate<Dictionary<string, MultiTextureMeshRef>>(capi, "armorMeshRefs", () => new Dictionary<string, MultiTextureMeshRef>());
            string key = "armorModelRef-" + itemstack.Collectible.Code.ToString();
            if (!orCreate.TryGetValue(key, out renderinfo.ModelRef))
            {
                ITexPositionSource texSource = capi.Tesselator.GetTextureSource(itemstack.Item, false);
                MeshData mesh = this.genMeshRef(capi, itemstack, renderinfo);
                renderinfo.ModelRef = (orCreate[key] = ( (mesh == null) ? renderinfo.ModelRef : capi.Render.UploadMultiTextureMesh(mesh)));
            }

            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        }

        // called when object is loaded
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            string value = base.Attributes["clothescategory"].AsString("waist");
            EnumCharacterDressType dresstype = EnumCharacterDressType.Unknown;
            Enum.TryParse<EnumCharacterDressType>(value, true, out dresstype);
            this.DressType = dresstype;
        }        

        public override void OnUnloaded(ICoreAPI api)
        {            
            base.OnUnloaded(api);            
        }

        private MeshData genMeshRef(ICoreClientAPI capi, ItemStack itemstack, ItemRenderInfo renderinfo)
        {
            //MeshRef modelRef = renderinfo.ModelRef;
            JsonObject attributes = itemstack.Collectible.Attributes;
            EntityProperties entityType = capi.World.GetEntityType(new AssetLocation("player"));
            Shape loadedShape = entityType.Client.LoadedShape;
            AssetLocation @base = entityType.Client.Shape.Base;
            Shape shape = new Shape
            {
                Elements = loadedShape.CloneElements(),
                Animations = loadedShape.Animations,
                AnimationsByCrc32 = loadedShape.AnimationsByCrc32,
                AttachmentPointsByCode = loadedShape.AttachmentPointsByCode,
                JointsById = loadedShape.JointsById,
                TextureWidth = loadedShape.TextureWidth,
                TextureHeight = loadedShape.TextureHeight,
                Textures = null
            };
            CompositeShape compositeShape = (!attributes["attachShape"].Exists) ? ((itemstack.Class == EnumItemClass.Item) ? itemstack.Item.Shape : itemstack.Block.Shape) : attributes["attachShape"].AsObject<CompositeShape>(null, itemstack.Collectible.Code.Domain);
            if (compositeShape == null)
            {
                capi.World.Logger.Warning("Entity armor {0} {1} does not define a shape through either the shape property or the attachShape Attribute. Armor pieces will be invisible.", new object[]
				{
					itemstack.Class,
					itemstack.Collectible.Code
				});
                return null;
            }
            AssetLocation assetLocation = compositeShape.Base.CopyWithPath("shapes/" + compositeShape.Base.Path + ".json");
            IAsset asset = capi.Assets.TryGet(assetLocation, true);
            if (asset == null)
            {
                capi.World.Logger.Warning("Entity wearable shape {0} defined in {1} {2} not found, was supposed to be at {3}. Armor piece will be invisible.", new object[]
				{
					compositeShape.Base,
					itemstack.Class,
					itemstack.Collectible.Code,
					assetLocation
				});
                return null;
            }
            Shape shape2;
            try
            {
                shape2 = asset.ToObject<Shape>(null);
            }
            catch (Exception ex)
            {
                capi.World.Logger.Warning("Exception thrown when trying to load entity armor shape {0} defined in {1} {2}. Armor piece will be invisible. Exception: {3}", new object[]
				{
					compositeShape.Base,
					itemstack.Class,
					itemstack.Collectible.Code,
					ex
				});
                return null;
            }
            shape.Textures = shape2.Textures;
            if (shape2.Textures.Count > 0 && shape2.TextureSizes.Count == 0)
            {
                foreach (KeyValuePair<string, AssetLocation> keyValuePair in shape2.Textures)
                {
                    shape2.TextureSizes.Add(keyValuePair.Key, new int[]
					{
						shape2.TextureWidth,
						shape2.TextureHeight
					});
                }
            }
            foreach (KeyValuePair<string, int[]> keyValuePair2 in shape2.TextureSizes)
            {
                shape.TextureSizes[keyValuePair2.Key] = keyValuePair2.Value;
            }
            foreach (ShapeElement shapeElement in shape2.Elements)
            {
                if (shapeElement.StepParentName != null)
                {
                    ShapeElement elementByName = shape.GetElementByName(shapeElement.StepParentName, StringComparison.InvariantCultureIgnoreCase);
                    if (elementByName == null)
                    {
                        capi.World.Logger.Warning("Entity wearable shape {0} defined in {1} {2} requires step parent element with name {3}, but no such element was found in shape {3}. Will not be visible.", new object[]
						{
							compositeShape.Base,
							itemstack.Class,
							itemstack.Collectible.Code,
							shapeElement.StepParentName,
							@base
						});
                    }
                    else if (elementByName.Children == null)
                    {
                        elementByName.Children = new ShapeElement[]
						{
							shapeElement
						};
                    }
                    else
                    {
                        elementByName.Children = elementByName.Children.Append(shapeElement);
                    }
                }
                else
                {
                    capi.World.Logger.Warning("Entity wearable shape element {0} in shape {1} defined in {2} {3} did not define a step parent element. Will not be visible.", new object[]
					{
						shapeElement.Name,
						compositeShape.Base,
						itemstack.Class,
						itemstack.Collectible.Code
					});
                }
            }
            ITexPositionSource textureSource = capi.Tesselator.GetTextureSource(itemstack.Item, false);            
            MeshData data;
            capi.Tesselator.TesselateShapeWithJointIds("entity", shape, out data, textureSource, new Vec3f(), null, null);
            return data;
        }
        
    }
}
