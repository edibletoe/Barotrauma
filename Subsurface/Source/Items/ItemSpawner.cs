﻿using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ItemSpawner
    {
        private readonly Queue<Pair<ItemPrefab, object>> spawnQueue;


        public List<Item> spawnItems = new List<Item>();


        public ItemSpawner()
        {
            spawnQueue = new Queue<Pair<ItemPrefab, object>>();
        }

        public void QueueItem(ItemPrefab itemPrefab, Vector2 worldPosition, bool isNetworkMessage = false)
        {
            if (!isNetworkMessage && GameMain.Client != null)
            {
                //clients aren't allowed to spawn new items unless the server says so
                return;
            }

            var itemInfo = new Pair<ItemPrefab, object>();
            itemInfo.First = itemPrefab;
            itemInfo.Second = worldPosition;

            spawnQueue.Enqueue(itemInfo);
        }

        public void QueueItem(ItemPrefab itemPrefab, Inventory inventory, bool isNetworkMessage = false)
        {
            if (!isNetworkMessage && GameMain.Client != null)
            {
                //clients aren't allowed to spawn new items unless the server says so
                return;
            }

            var itemInfo = new Pair<ItemPrefab, object>();
            itemInfo.First = itemPrefab;
            itemInfo.Second = inventory;

            spawnQueue.Enqueue(itemInfo);
        }

        public void Update()
        {
            if (!spawnQueue.Any()) return;

            List<Item> items = new List<Item>();
            //List<Inventory> inventories = new List<Inventory>();

            while (spawnQueue.Count>0)
            {
                var itemInfo = spawnQueue.Dequeue();

                if (itemInfo.Second is Vector2)
                {
                    var item = new Item(itemInfo.First, (Vector2)itemInfo.Second, null);
                    AddToSpawnedList(item);

                    items.Add(item);
                }
                else if (itemInfo.Second is Inventory)
                {
                    var item = new Item(itemInfo.First, Vector2.Zero, null);
                    AddToSpawnedList(item);

                    var inventory = (Inventory)itemInfo.Second;
                    inventory.TryPutItem(item, null);

                    items.Add(item);
                }
            }
            
        }

        public void AddToSpawnedList(Item item)
        {
            spawnItems.Add(item);
        }

        public void Clear()
        {
            spawnQueue.Clear();
            spawnItems.Clear();
        }
    }

    class ItemRemover
    {
        private readonly Queue<Item> removeQueue;
        
        public List<Item> removedItems = new List<Item>();

        public ItemRemover()
        {
            removeQueue = new Queue<Item>();
        }

        public void QueueItem(Item item, bool isNetworkMessage = false)
        {
            if (!isNetworkMessage && GameMain.Client != null)
            {
                //clients aren't allowed to remove items unless the server says so
                return;
            }

            removeQueue.Enqueue(item);
        }

        public void Update()
        {
            if (!removeQueue.Any()) return;

            List<Item> items = new List<Item>();

            while (removeQueue.Count > 0)
            {
                var item = removeQueue.Dequeue();
                removedItems.Add(item);

                item.Remove();

                items.Add(item);
            }
            
        }
        
        public void Clear()
        {
            removeQueue.Clear();
            removedItems.Clear();
        }
    }
}
