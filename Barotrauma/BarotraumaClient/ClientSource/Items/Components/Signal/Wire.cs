﻿using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Wire : ItemComponent, IDrawableComponent, IServerSerializable, IClientSerializable
    {
        private readonly struct ClientEventData : IEventData
        {
            public readonly int NodeCount;
            
            public ClientEventData(int nodeCount)
            {
                NodeCount = nodeCount;
            }
        }
        
        public static Color higlightColor = Color.LightGreen;
        public static Color editorHighlightColor = Color.Yellow;
        public static Color editorSelectedColor = Color.Red;

        partial class WireSection
        {
            public VertexPositionColorTexture[] vertices;
            public VertexPositionColorTexture[] shiftedVertices;

            private float cachedWidth = 0f;

            private void RecalculateVertices(Wire wire, float width)
            {
                if (MathUtils.NearlyEqual(cachedWidth, width)) { return; }
                cachedWidth = width;

                vertices = new VertexPositionColorTexture[4];

                Vector2 expandDir = start-end;
                expandDir.Normalize();
                float temp = expandDir.X;
                expandDir.X = -expandDir.Y;
                expandDir.Y = -temp;

                Rectangle srcRect = wire.wireSprite.SourceRect;

                expandDir *= width * srcRect.Height * 0.5f;

                Vector2 rectLocation = srcRect.Location.ToVector2();
                Vector2 rectSize = srcRect.Size.ToVector2();
                Vector2 textureSize = new Vector2(wire.wireSprite.Texture.Width, wire.wireSprite.Texture.Height);

                Vector2 topLeftUv = rectLocation / textureSize;
                Vector2 bottomRightUv = (rectLocation + rectSize) / textureSize;

                Vector2 invStart = new Vector2(start.X, -start.Y);
                Vector2 invEnd = new Vector2(end.X, -end.Y);

                vertices[0] = new VertexPositionColorTexture(new Vector3(invStart + expandDir, 0f), Color.White, topLeftUv);
                vertices[2] = new VertexPositionColorTexture(new Vector3(invEnd + expandDir, 0f), Color.White, new Vector2(bottomRightUv.X, topLeftUv.Y));
                vertices[1] = new VertexPositionColorTexture(new Vector3(invStart - expandDir, 0f), Color.White, new Vector2(topLeftUv.X, bottomRightUv.Y));
                vertices[3] = new VertexPositionColorTexture(new Vector3(invEnd - expandDir, 0f), Color.White, bottomRightUv);

                shiftedVertices = (VertexPositionColorTexture[])vertices.Clone();
            }

            public void Draw(SpriteBatch spriteBatch, Wire wire, Color color, Vector2 offset, float depth, float width = 0.3f)
            {
                if (width <= 0f) { return; }
                RecalculateVertices(wire, width);

                for (int i = 0; i < vertices.Length; i++)
                {
                    shiftedVertices[i].Color = color;
                    shiftedVertices[i].Position = vertices[i].Position;
                    shiftedVertices[i].Position.X += offset.X;
                    shiftedVertices[i].Position.Y -= offset.Y;
                }
                spriteBatch.Draw(wire.wireSprite.Texture,
                    shiftedVertices,
                    depth);
            }

            public static void Draw(SpriteBatch spriteBatch, Wire wire, Vector2 start, Vector2 end, Color color, float depth, float width = 0.3f)
            {
                start.Y = -start.Y;
                end.Y = -end.Y;
                
                spriteBatch.Draw(wire.wireSprite.Texture,
                    start, wire.wireSprite.SourceRect, color,
                    MathUtils.VectorToAngle(end - start),
                    new Vector2(0.0f, wire.wireSprite.size.Y / 2.0f),
                    new Vector2((Vector2.Distance(start, end)) / wire.wireSprite.size.X, width),
                    SpriteEffects.None,
                    depth);
            }
        }
        private static Sprite defaultWireSprite;
        private Sprite overrideSprite;
        private Sprite wireSprite;

        private static Wire draggingWire;
        private static int? selectedNodeIndex;
        private static int? highlightedNodeIndex;

        [Serialize(0.3f, IsPropertySaveable.No)]
        public float Width
        {
            get;
            set;
        }

        public Vector2 DrawSize
        {
            get { return sectionExtents; }
        }

        public static Wire DraggingWire
        {
            get => draggingWire;
        }

        partial void InitProjSpecific(ContentXElement element)
        {
            if (defaultWireSprite == null)
            {
                defaultWireSprite = new Sprite("Content/Items/Electricity/signalcomp.png", new Rectangle(970, 47, 14, 16), new Vector2(0.5f, 0.5f))
                {
                    Depth = 0.855f
                };
            }

            foreach (var subElement in element.Elements())
            {
                if (subElement.Name.ToString().Equals("wiresprite", StringComparison.OrdinalIgnoreCase))
                {
                    overrideSprite = new Sprite(subElement);
                    break;
                }
            }

            wireSprite = overrideSprite ?? defaultWireSprite;
        }


        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1)
        {
            Draw(spriteBatch, editing, Vector2.Zero, itemDepth);
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, Vector2 offset, float itemDepth = -1)
        {
            if (sections.Count == 0 && !IsActive || Hidden)
            {
                Drawable = false;
                return;
            }

            Vector2 drawOffset = Vector2.Zero;
            Submarine sub = item.Submarine;
            if (IsActive && sub == null) // currently being rewired, we need to get the sub from the connections in case the wire has been taken outside
            {
                if (connections[0] != null && connections[0].Item.Submarine != null) { sub = connections[0].Item.Submarine; }
                if (connections[1] != null && connections[1].Item.Submarine != null) { sub = connections[1].Item.Submarine; }
            }

            if (sub != null)
            {
                drawOffset = sub.DrawPosition + sub.HiddenSubPosition;
            }

            drawOffset += offset;

            float baseDepth = UseSpriteDepth ? item.SpriteDepth : wireSprite.Depth;
            float depth = item.IsSelected ? 0.0f : SubEditorScreen.IsWiringMode() ? 0.02f : baseDepth + (item.ID % 100) * 0.000001f;// item.GetDrawDepth(wireSprite.Depth, wireSprite);

            if (item.IsHighlighted)
            {
                foreach (WireSection section in sections)
                {
                    section.Draw(spriteBatch, this, Screen.Selected == GameMain.GameScreen ? higlightColor : editorHighlightColor, drawOffset, depth + 0.00001f, Width * 2.0f);
                }
            }
            else if (item.IsSelected)
            {
                foreach (WireSection section in sections)
                {
                    section.Draw(spriteBatch, this, editorSelectedColor, drawOffset, depth + 0.00001f, Width * 2.0f);
                }
            }

            foreach (WireSection section in sections)
            {
                section.Draw(spriteBatch, this, item.Color, drawOffset, depth, Width);
            }

            if (nodes.Count > 0)
            {
                if (!IsActive)
                {
                    if (connections[0] == null) { DrawHangingWire(spriteBatch, nodes[0] + drawOffset, depth); }
                    if (connections[1] == null) { DrawHangingWire(spriteBatch, nodes.Last() + drawOffset, depth); }
                }
                if (IsActive && item.ParentInventory?.Owner is Character user && user == Character.Controlled)// && Vector2.Distance(newNodePos, nodes[nodes.Count - 1]) > nodeDistance)
                {
                    if (user.CanInteract && currLength < MaxLength)
                    {
                        Vector2 gridPos = Character.Controlled.Position;
                        Vector2 roundedGridPos = new Vector2(
                            MathUtils.RoundTowardsClosest(Character.Controlled.Position.X, Submarine.GridSize.X),
                            MathUtils.RoundTowardsClosest(Character.Controlled.Position.Y, Submarine.GridSize.Y));
                        //Vector2 attachPos = GetAttachPosition(user);

                        if (item.Submarine == null)
                        {
                            Structure attachTarget = Structure.GetAttachTarget(item.WorldPosition);
                            if (attachTarget != null)
                            {
                                if (attachTarget.Submarine != null)
                                {
                                    //set to submarine-relative position
                                    gridPos += attachTarget.Submarine.Position;
                                    roundedGridPos += attachTarget.Submarine.Position;
                                }
                            }
                        }
                        else
                        {
                            gridPos += item.Submarine.Position;
                            roundedGridPos += item.Submarine.Position;
                        }

                        if (!SubEditorScreen.IsSubEditor() || !SubEditorScreen.ShouldDrawGrid)
                        {
                            Submarine.DrawGrid(spriteBatch, 14, gridPos, roundedGridPos, alpha: 0.25f);
                        }

                        WireSection.Draw(
                            spriteBatch, this,
                            new Vector2(nodes[nodes.Count - 1].X, nodes[nodes.Count - 1].Y) + drawOffset,
                            new Vector2(newNodePos.X, newNodePos.Y) + drawOffset,
                            item.Color, 0.0f, Width);

                        WireSection.Draw(
                            spriteBatch, this,
                            new Vector2(newNodePos.X, newNodePos.Y) + drawOffset,
                            item.DrawPosition,
                            item.Color, itemDepth, Width);

                        GUI.DrawRectangle(spriteBatch, new Vector2(newNodePos.X + drawOffset.X, -(newNodePos.Y + drawOffset.Y)) - Vector2.One * 3, Vector2.One * 6, item.Color);
                    }
                    else
                    {
                        WireSection.Draw(
                            spriteBatch, this,
                            new Vector2(nodes[nodes.Count - 1].X, nodes[nodes.Count - 1].Y) + drawOffset,
                            item.DrawPosition,
                            item.Color, 0.0f, Width);
                    }
                }
            }

            if (!editing || !GameMain.SubEditorScreen.WiringMode) { return; }

            for (int i = 0; i < nodes.Count; i++)
            {
                Vector2 drawPos = nodes[i];
                if (item.Submarine != null) drawPos += item.Submarine.Position + item.Submarine.HiddenSubPosition;
                drawPos.Y = -drawPos.Y;

                if ((highlightedNodeIndex == i && item.IsHighlighted) || (selectedNodeIndex == i && item.IsSelected))
                {
                    GUI.DrawRectangle(spriteBatch, drawPos + new Vector2(-10, -10), new Vector2(20, 20), editorHighlightColor, false, 0.0f);
                }

                if (item.IsSelected)
                {
                    GUI.DrawRectangle(spriteBatch, drawPos + new Vector2(-5, -5), new Vector2(10, 10), item.Color, true, 0.0f);

                }
                else
                {
                    GUI.DrawRectangle(spriteBatch, drawPos + new Vector2(-3, -3), new Vector2(6, 6), item.Color, true, 0.015f);
                }
            }
        }

        private void DrawHangingWire(SpriteBatch spriteBatch, Vector2 start, float depth)
        {
            float angle = (float)Math.Sin(GameMain.GameScreen.GameTime * 2.0f + item.ID) * 0.2f;
            Vector2 endPos = start + new Vector2((float)Math.Sin(angle), -(float)Math.Cos(angle)) * 50.0f;

            WireSection.Draw(
                spriteBatch, this,
                start, endPos,
                GUIStyle.Orange, depth + 0.00001f, 0.2f);

            WireSection.Draw(
                spriteBatch, this,
                start, start + (endPos - start) * 0.7f,
                item.Color, depth, 0.3f);
        }

        public static void UpdateEditing(List<Wire> wires)
        {
            var doubleClicked = PlayerInput.DoubleClicked();

            Wire equippedWire = Character.Controlled.HeldItems.FirstOrDefault(it => it.GetComponent<Wire>() != null)?.GetComponent<Wire>();
            if (equippedWire != null && GUI.MouseOn == null)
            {
                if (PlayerInput.PrimaryMouseButtonClicked() && Character.Controlled.SelectedConstruction == null)
                {
                    equippedWire.Use(1.0f, Character.Controlled);
                }
                return;
            }

            //dragging a node of some wire
            if (draggingWire != null && !doubleClicked)
            {
                if (Character.Controlled != null)
                {
                    Character.Controlled.FocusedItem = null;
                    Character.Controlled.ResetInteract = true;
                    Character.Controlled.ClearInputs();
                }
                //cancel dragging
                if (!PlayerInput.PrimaryMouseButtonHeld())
                {
                    draggingWire = null;
                    selectedNodeIndex = null;
                }
                //update dragging
                else
                {
                    MapEntity.DisableSelect = true;

                    Submarine sub = draggingWire.item.Submarine;
                    if (draggingWire.connections[0] != null && draggingWire.connections[0].Item.Submarine != null) sub = draggingWire.connections[0].Item.Submarine;
                    if (draggingWire.connections[1] != null && draggingWire.connections[1].Item.Submarine != null) sub = draggingWire.connections[1].Item.Submarine;

                    Vector2 nodeWorldPos = GameMain.SubEditorScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                    if (sub != null)
                    {
                        nodeWorldPos = nodeWorldPos - sub.HiddenSubPosition - sub.Position;
                    }

                    if (selectedNodeIndex.HasValue && selectedNodeIndex.Value >= draggingWire.nodes.Count) { selectedNodeIndex = null; }
                    if (highlightedNodeIndex.HasValue && highlightedNodeIndex.Value >= draggingWire.nodes.Count) { highlightedNodeIndex = null; }

                    if (selectedNodeIndex.HasValue)
                    {
                        if (!PlayerInput.IsShiftDown())
                        {
                            nodeWorldPos.X = MathUtils.Round(nodeWorldPos.X, Submarine.GridSize.X / 2.0f);
                            nodeWorldPos.Y = MathUtils.Round(nodeWorldPos.Y, Submarine.GridSize.Y / 2.0f);
                        }

                        draggingWire.nodes[(int)selectedNodeIndex] = nodeWorldPos;
                        draggingWire.UpdateSections();
                    }
                    else
                    {
                        float dragDistance = Submarine.GridSize.X * Submarine.GridSize.Y;
                        dragDistance *= 0.5f;
                        if ((highlightedNodeIndex.HasValue && Vector2.DistanceSquared(nodeWorldPos, draggingWire.nodes[(int)highlightedNodeIndex]) >= dragDistance) || 
                            PlayerInput.IsShiftDown())
                        {
                            selectedNodeIndex = highlightedNodeIndex;
                        }
                    }

                    MapEntity.SelectEntity(draggingWire.item);
                }

                return;
            }

            bool updateHighlight = true;

            //a wire has been selected -> check if we should start dragging one of the nodes
            float nodeSelectDist = 10, sectionSelectDist = 5;
            highlightedNodeIndex = null;
            if (MapEntity.SelectedList.Count == 1 && MapEntity.SelectedList.FirstOrDefault() is Item selectedItem)
            {
                Wire selectedWire = selectedItem.GetComponent<Wire>();

                if (selectedWire != null)
                {
                    Vector2 mousePos = GameMain.SubEditorScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                    if (selectedWire.item.Submarine != null) mousePos -= (selectedWire.item.Submarine.Position + selectedWire.item.Submarine.HiddenSubPosition);

                    //left click while holding ctrl -> check if the cursor is on a wire section, 
                    //and add a new node if it is
                    if (PlayerInput.KeyDown(Keys.RightControl) || PlayerInput.KeyDown(Keys.LeftControl))
                    {
                        if (PlayerInput.PrimaryMouseButtonClicked())
                        {
                            if (Character.Controlled != null)
                            {
                                Character.Controlled.ResetInteract = true;
                                Character.Controlled.ClearInputs();
                            }
                            int closestSectionIndex = selectedWire.GetClosestSectionIndex(mousePos, sectionSelectDist, out _);
                            if (closestSectionIndex > -1)
                            {
                                selectedWire.nodes.Insert(closestSectionIndex + 1, mousePos);
                                selectedWire.UpdateSections();
                            }
                        }
                    }
                    else
                    {
                        //check if close enough to a node
                        int closestIndex = selectedWire.GetClosestNodeIndex(mousePos, nodeSelectDist, out _);
                        if (closestIndex > -1)
                        {
                            highlightedNodeIndex = closestIndex;

                            Vector2 nudge = MapEntity.GetNudgeAmount(doHold: false);
                            if (nudge != Vector2.Zero && closestIndex < selectedWire.nodes.Count)
                            {
                                selectedWire.MoveNode(closestIndex, nudge);
                            }

                            //start dragging the node
                            if (PlayerInput.PrimaryMouseButtonHeld())
                            {
                                if (Character.Controlled != null)
                                {
                                    Character.Controlled.ResetInteract = true;
                                    Character.Controlled.ClearInputs();
                                }
                                draggingWire = selectedWire;
                                //selectedNodeIndex = closestIndex;
                                return;
                            }
                            //remove the node
                            else if (PlayerInput.SecondaryMouseButtonClicked() && closestIndex > 0 && closestIndex < selectedWire.nodes.Count - 1)
                            {
                                selectedWire.nodes.RemoveAt(closestIndex);
                                selectedWire.UpdateSections();
                            } 
                            // if only one end of the wire is disconnect pick it back up with double click
                            else if (doubleClicked && equippedWire == null && Character.Controlled != null && selectedWire.connections.Any(conn => conn != null))
                            {
                                if (selectedWire.connections[0] == null && closestIndex == 0 || selectedWire.connections[1] == null && closestIndex == selectedWire.nodes.Count - 1)
                                {
                                    selectedWire.IsActive = true;
                                    selectedWire.nodes.RemoveAt(closestIndex);
                                    selectedWire.UpdateSections();
                                    
                                    // flip the wire
                                    if (closestIndex == 0)
                                    {
                                        selectedWire.nodes.Reverse();
                                        selectedWire.connections[0] = selectedWire.connections[1];
                                        selectedWire.connections[1] = null;
                                    }
                                
                                    selectedWire.shouldClearConnections = false;
                                    Character.Controlled.Inventory.TryPutItem(selectedWire.item, Character.Controlled, new List<InvSlotType> { InvSlotType.LeftHand, InvSlotType.RightHand });
                                    foreach (var entity in MapEntity.mapEntityList)
                                    {
                                        if (entity is Item item)
                                        {
                                            item.GetComponent<ConnectionPanel>()?.DisconnectedWires.Remove(selectedWire);
                                        }
                                    }
                                    MapEntity.SelectedList.Clear();
                                    selectedWire.shouldClearConnections = true;
                                    updateHighlight = false;
                                }
                            }
                        }
                    }
                }
            }

            Wire highlighted = null;

            //check which wire is highlighted with the cursor
            if (GUI.MouseOn == null)
            {
                float closestDist = float.PositiveInfinity;
                foreach (Wire w in wires)
                {
                    Vector2 mousePos = GameMain.SubEditorScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                    if (w.item.Submarine != null) { mousePos -= (w.item.Submarine.Position + w.item.Submarine.HiddenSubPosition); }

                    int highlightedNode = w.GetClosestNodeIndex(mousePos, highlighted == null ? nodeSelectDist : closestDist, out float dist);
                    if (highlightedNode > -1)
                    {
                        if (dist < closestDist)
                        {
                            highlightedNodeIndex = highlightedNode;
                            highlighted = w;
                            closestDist = dist;
                        }
                    }

                    if (w.GetClosestSectionIndex(mousePos, highlighted == null ? sectionSelectDist : closestDist, out dist) > -1)
                    {
                        //prefer nodes over sections
                        if (dist + nodeSelectDist * 0.5f < closestDist)
                        {
                            highlightedNodeIndex = null;
                            highlighted = w;
                            closestDist = dist + nodeSelectDist * 0.5f;
                        }
                    }
                }
            }

            if (highlighted != null && updateHighlight)
            {
                highlighted.item.IsHighlighted = true;
                if (PlayerInput.PrimaryMouseButtonClicked())
                {
                    MapEntity.DisableSelect = true;
                    MapEntity.SelectEntity(highlighted.item);
                }
            }
        }

        public override void Move(Vector2 amount, bool ignoreContacts = false)
        {
            //only used in the sub editor, hence only in the client project
            if (!item.IsSelected) { return; }

            Vector2 wireNodeOffset = item.Submarine == null ? Vector2.Zero : item.Submarine.HiddenSubPosition + amount;            
            for (int i = 0; i < nodes.Count; i++)
            {
                if (i == 0 || i == nodes.Count - 1)
                {
                    if (connections[0]?.Item != null && !connections[0].Item.IsSelected && 
                        (Submarine.RectContains(connections[0].Item.Rect, nodes[i] + wireNodeOffset) || Submarine.RectContains(connections[0].Item.Rect, nodes[i] + wireNodeOffset - amount)))
                    {
                        continue;
                    }
                    else if (connections[1]?.Item != null && !connections[1].Item.IsSelected && 
                        (Submarine.RectContains(connections[1].Item.Rect, nodes[i] + wireNodeOffset) || Submarine.RectContains(connections[1].Item.Rect, nodes[i] + wireNodeOffset - amount)))
                    {
                        continue;
                    }
                }                
                nodes[i] += amount;
            }
            UpdateSections();
        }
        public bool IsMouseOn()
        {
            if (GUI.MouseOn == null)
            {
                Vector2 mousePos = GameMain.SubEditorScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                if (item.Submarine != null) { mousePos -= (item.Submarine.Position + item.Submarine.HiddenSubPosition); }

                if (GetClosestNodeIndex(mousePos, 10, out _) > -1) { return true; }
                if (GetClosestSectionIndex(mousePos, 10, out _) > -1) { return true; }
            }

            return false;
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            int eventIndex = msg.ReadRangedInteger(0, (int)Math.Ceiling(MaxNodeCount / (float)MaxNodesPerNetworkEvent));
            int nodeCount = msg.ReadRangedInteger(0, MaxNodesPerNetworkEvent);
            int nodeStartIndex = eventIndex * MaxNodesPerNetworkEvent;

            Vector2[] nodePositions = new Vector2[nodeStartIndex + nodeCount];
            for (int i = 0; i < nodes.Count && i < nodePositions.Length; i++)
            {
                nodePositions[i] = nodes[i];
            }

            for (int i = 0; i < nodeCount; i++)
            {
                nodePositions[nodeStartIndex + i] = new Vector2(msg.ReadSingle(), msg.ReadSingle());
            }

            if (nodePositions.Any(n => !MathUtils.IsValid(n)))
            {
                nodes.Clear();
                return;
            }

            nodes = nodePositions.ToList();
            UpdateSections();
            Drawable = nodes.Any();
            IsActive = 
                (connections[0] == null ^ connections[1] == null) &&
                (item.ParentInventory is CharacterInventory characterInventory && ((characterInventory.Owner as Character)?.HasEquippedItem(item) ?? false));
        }

        public override bool ValidateEventData(NetEntityEvent.IData data)
            => TryExtractEventData<ClientEventData>(data, out _);

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null)
        {
            var eventData = ExtractEventData<ClientEventData>(extraData);
            int nodeCount = eventData.NodeCount;
            msg.Write((byte)nodeCount);
            if (nodeCount > 0)
            {
                msg.Write(nodes.Last().X);
                msg.Write(nodes.Last().Y);
            }
        }
    }
}
