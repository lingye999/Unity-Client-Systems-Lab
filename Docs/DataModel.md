# 背包数据模型和变量定义

本文档定义 Day 2 的四个核心对象。字段含义一旦确定，后续功能只能通过明确的迁移或版本变更修改。

## 对象关系

```text
ItemDefinition  --itemId-->  ItemInstance  -->  InventorySlot  -->  Inventory
```

`ItemDefinition` 描述物品类型，`ItemInstance` 描述玩家拥有的运行时记录，`InventorySlot` 描述位置，`Inventory` 描述整个容器。

## ItemDefinition

静态配置，使用 Unity `ScriptableObject` 保存：

| 字段 | 类型 | 约束 |
| --- | --- | --- |
| `itemId` | `int` | 大于 0，全局唯一，发布后不可复用 |
| `displayName` | `string` | 非空，面向玩家显示 |
| `category` | `ItemCategory` | `Weapon`、`Consumable`、`Material`、`Quest` |
| `rarity` | `ItemRarity` | `Common`、`Uncommon`、`Rare`、`Epic`、`Legendary` |
| `icon` | `Sprite` | 运行时展示资源，可后续迁移到 Addressables |
| `isStackable` | `bool` | 是否允许同类物品进入同一堆 |
| `maxStackSize` | `int` | 大于 0；不可堆叠物品固定为 1 |
| `description` | `string` | 可为空的展示说明 |

`ItemDefinition` 不保存玩家数量、位置、等级或是否已读。

## ItemInstance

运行时和存档数据。对于可堆叠物品，一个实例代表一堆；对于装备，一个实例代表一件独立物品：

| 字段 | 类型 | 约束 |
| --- | --- | --- |
| `instanceId` | `string` | 非空、唯一、由业务层生成 |
| `itemId` | `int` | 必须能在配置仓库中找到对应定义 |
| `quantity` | `int` | 大于 0，不超过定义的 `maxStackSize` |
| `level` | `int` | 当前版本可选；未实现装备成长前不参与规则 |

`itemId` 标识类型，`instanceId` 标识运行时记录。两把相同的剑共享 `itemId`，但必须拥有不同的 `instanceId`。

## InventorySlot

背包中的固定位置：

| 字段 | 类型 | 约束 |
| --- | --- | --- |
| `slotIndex` | `int` | 从 0 开始，范围为 `0 <= slotIndex < capacity` |
| `item` | `ItemInstance?` | `null` 表示空格子 |

格子索引是稳定位置，不用物品 ID 代替。移动和交换操作都以 `slotIndex` 为输入。

## Inventory

背包容器：

| 字段 | 类型 | 约束 |
| --- | --- | --- |
| `capacity` | `int` | 大于 0；第一版默认 20 |
| `slots` | `IReadOnlyList<InventorySlot>` | 数量必须等于 `capacity` |

`Inventory` 负责维护以下不变量：没有重复的 `slotIndex`；非空格子的物品数量合法；同一独立实例不能同时出现在多个格子；所有 `itemId` 都能解析到配置。

## 枚举

```csharp
public enum ItemCategory
{
    Weapon,
    Consumable,
    Material,
    Quest
}

public enum ItemRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}
```

## 延后字段

`isNew`、装备属性、耐久度、绑定状态和红点状态不放入 Day 2 的基础模型。它们要在确认业务规则后，分别归入运行时状态、装备扩展或红点模块，避免所有功能共享一个不断膨胀的物品类。
