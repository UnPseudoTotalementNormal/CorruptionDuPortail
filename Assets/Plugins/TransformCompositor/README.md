# Transform Compositor

A Unity plugin for compositing multiple animation layers on a single Transform. Allows multiple animations to modify position, rotation, and scale simultaneously without conflicts.

## Features

- **Layer-based Animation**: Multiple named layers can contribute to the final transformation
- **Additive Composition**: Position and rotation are added together
- **Multiplicative Scale**: Scale values are multiplied for correct scaling behavior
- **DOTween Integration**: Full DOTween support with extension methods
- **Manual Control**: Set layer values directly in code
- **Inspector Debugging**: All layers are serializable and visible in the Inspector

## Installation

Simply copy the `TransformCompositor` folder into your Unity project's `Assets/Plugins` directory.

### DOTween Integration 

You can also control layers using DOTween for smooth animations.
It will be enabled automatically if DOTween is detected.

## Quick Start

### 1. Add the Component

```csharp
using TransformComposition;

// Add to your GameObject
var compositor = gameObject.AddComponent<TransformCompositorComponent>();
```

Or inherit from `TransformCompositorComponent` in your custom MonoBehaviour.

### 2. Animate Layers

```csharp
// Get or create a layer
var hoverLayer = compositor.GetLayer("Hover");
var flipLayer = compositor.GetLayer("Flip");

// Animate with DOTween
hoverLayer.DOLocalMoveY(0.35f, 0.35f).SetEase(Ease.OutQuint);
hoverLayer.DOScale(1.15f, 0.35f).SetEase(Ease.OutQuint);

flipLayer.DOLocalMoveY(4f, 0.5f).SetEase(Ease.OutQuint);
flipLayer.DOLocalRotate(new Vector3(0, 0, 180), 0.75f);
```

### 3. Manual Control

```csharp
// Set values directly
var customLayer = compositor.GetLayer("Custom");
customLayer.localPosition = new Vector3(0, 1, 0);
customLayer.localScale = Vector3.one * 2f;
customLayer.rotationZ = 45f;
```

## How It Works

### Composition Rules

#### Global Mode (Default)

- **Rotation**: All layer rotations (quaternions) are **multiplied** together
- **Scale**: All layer scales are **multiplied** component-wise

Example:
```csharp
- **Rotation**: All layer rotations (euler angles) are **added** together
- **Scale**: All layer scales are **multiplied** together
// Result: position (0, 2.5, 0), scale (1.265, 1.265, 1.265)
```

#### Local Mode

Transformations are applied in the **local space of previous layers**, simulating a parent-child transform hierarchy:

Main component to attach to GameObjects.

```csharp
// Get or create a layer
TransformLayer GetLayer(string layerName)

// Check if layer exists
bool HasLayer(string layerName)

// Remove a layer
void RemoveLayer(string layerName)

// Remove all layers
void ClearLayers()

// Manually apply composition (useful if autoUpdate is disabled)
void ApplyComposedTransform()
```

### TransformLayer

Represents a single animation layer.

```csharp
// Main properties
Vector3 localPosition
Vector3 localEulerAngles
Vector3 localScale
Quaternion localRotation

// Individual component accessors
float positionX, positionY, positionZ
float rotationX, rotationY, rotationZ
float scaleX, scaleY, scaleZ

// Reset to default values
void Reset()
```

### DOTween Extensions

All standard DOTween methods are available:

```csharp
layer.DOLocalMove(Vector3, duration)
layer.DOLocalMoveX/Y/Z(float, duration)
layer.DOLocalRotate(Vector3, duration, RotateMode)
layer.DOScale(Vector3, duration)
layer.DOScale(float, duration)  // Uniform scale
layer.DOScaleX/Y/Z(float, duration)
layer.DOPunchScale(Vector3, duration, vibrato, elasticity)
layer.DOKill(complete)
```

## Example: Card Hover and Flip

```csharp
using TransformComposition;
using DG.Tweening;
using UnityEngine;

public class CardAnimator : TransformCompositorComponent
{
    public void OnHover()
    {
        var hoverLayer = GetLayer("Hover");
        hoverLayer.DOLocalMoveY(0.35f, 0.35f).SetEase(Ease.OutQuint);
        hoverLayer.DOScale(1.15f, 0.35f).SetEase(Ease.OutQuint);
    }

    public void OnUnhover()
    {
        var hoverLayer = GetLayer("Hover");
        hoverLayer.DOLocalMoveY(0f, 0.35f).SetEase(Ease.OutQuint);
        hoverLayer.DOScale(1f, 0.35f).SetEase(Ease.OutQuint);
    }

    public void FlipCard()
    {
        var flipLayer = GetLayer("Flip");
        
        // Move up, rotate, then move down
        flipLayer.DOLocalMoveY(4f, 0.5f).SetEase(Ease.OutQuint)
            .OnComplete(() => flipLayer.DOLocalMoveY(0f, 0.5f).SetEase(Ease.OutQuint));
        
        flipLayer.DOLocalRotate(new Vector3(0, 0, 180), 0.75f);
    }
}
```

Notice how hover and flip animations can play simultaneously without conflict!

## Advanced Usage

### Disable Auto-Update

For performance optimization or custom update timing:

```csharp
compositor.autoUpdate = false;

// Call manually when needed
void Update()
{
    if (needsUpdate)
    {
        compositor.ApplyComposedTransform();
    }
}
```

### Access Composed Transform

Get the final composed values without applying:

```csharp
var composed = compositor.GetComposedTransform();
Debug.Log($"Final position: {composed.localPosition}");
Debug.Log($"Final scale: {composed.localScale}");
```

## License

Free to use in any project, commercial or non-commercial while crediting the original author. No warranty provided.

