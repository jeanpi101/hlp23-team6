﻿module SmartRotate
open Elmish
open Fable.React.Props
open CommonTypes
open Fable.React
open DrawModelType
open DrawModelType.SymbolT
open DrawModelType.BusWireT
open Symbol
open Optics
open Operators

open SmartHelpers

(*HLP23: AUTHOR Ismagilov
  SheetUpdate.fs: 'Rotate' and 'Flip' msg in update function is replaced with this Smart implentation of rotate and flip.
  DrawModelType.fs: Added type ScaleType in SymbolT Module, which Distinguishes the type of scaling the user does.

  Added 2 keyboard messages in Renderer (CrtlU & CrtrlI) to scale the block of symbols up and down respectively.
  Invalid placement handled by giving model action drag and drop, therefore requiring user to place down/continue changing until valid

  SmartHelpers.fs contains all helper functions, e.g Rotating/Flipping symbols or points in general about any center point,
  as opposed to original rotate/flip functions.
*)

/// <summary>HLP 23: AUTHOR Ismagilov - Rotates a block of symbols, returning the new symbol model</summary>
/// <param name="compList"> List of ComponentId's of selected components</param>
/// <param name="model"> Current symbol model</param>
/// <param name="rotation"> Type of rotation to do</param>
/// <returns>New rotated symbol model</returns>
let rotateBlock (compList:ComponentId list) (model:SymbolT.Model) (rotation:RotationType) = 

    let SelectedSymbols = List.map (fun x -> model.Symbols |> Map.find x) compList
    let UnselectedSymbols = model.Symbols |> Map.filter (fun x _ -> not (List.contains x compList))

    //Get block properties of selected symbols
    let block = SmartHelpers.getBlock SelectedSymbols

    //Rotated symbols about the center
    let newSymbols = 
        List.map (fun x -> SmartHelpers.rotateSymbolInBlock rotation (block.Centre()) x) SelectedSymbols 

    //return model with block of rotated selected symbols, and unselected symbols
    {model with Symbols = 
                ((Map.ofList (List.map2 (fun x y -> (x,y)) compList newSymbols)
                |> Map.fold (fun acc k v -> Map.add k v acc) UnselectedSymbols)
    )}

/// <summary>HLP 23: AUTHOR Ismagilov - Scales a block of symbols, returning the new symbol model</summary>
/// <param name="compList"> List of ComponentId's of selected components</param>
/// <param name="model"> Current symbol model</param>
/// <param name="scale"> Type of scaling to do</param>
/// <returns>New scaled symbol model</returns>
//Note: This scaling is kept here as part of original individual code, and is used with Ctrl+U, Ctrl+I
let scaleBlock (compList:ComponentId list) (model:SymbolT.Model) (scale:ScaleType)=
    ///Similar structure to rotateBlock, easy to understand

    let SelectedSymbols = List.map (fun x -> model.Symbols |> Map.find x) compList
    let UnselectedSymbols = model.Symbols |> Map.filter (fun x _ -> not (List.contains x compList))

    let block = SmartHelpers.getBlock SelectedSymbols
      
    let newSymbols = List.map (SmartHelpers.scaleSymbolInBlock scale block) SelectedSymbols

    {model with Symbols = 
                ((Map.ofList (List.map2 (fun x y -> (x,y)) compList newSymbols)
                |> Map.fold (fun acc k v -> Map.add k v acc) UnselectedSymbols)
    )}

/// <summary>HLP 23: AUTHOR Ismagilov - Scales a block of symbols, returning the new symbol model</summary>
/// <param name="compList"> List of ComponentId's of selected components</param>
/// <param name="model"> Current symbol model</param>
/// <param name="scale"> Type of scaling to do</param>
/// <returns>New scaled symbol model</returns>
//Note: This scaling is used for the new UI scaling block, and takes in a variable scale factor
let scaleBlockGroup (compList:ComponentId list) (model:SymbolT.Model) (mag:float)=
    //Similar structure to rotateBlock, easy to understand

    let SelectedSymbols = List.map (fun x -> model.Symbols |> Map.find x) compList
    let UnselectedSymbols = model.Symbols |> Map.filter (fun x _ -> not (List.contains x compList))

    let block = SmartHelpers.getBlock SelectedSymbols
      
    let newSymbols = List.map (SmartHelpers.scaleSymbolInBlockGroup mag block) SelectedSymbols

    {model with Symbols = 
                ((Map.ofList (List.map2 (fun x y -> (x,y)) compList newSymbols)
                |> Map.fold (fun acc k v -> Map.add k v acc) UnselectedSymbols)
    )}

/// <summary>HLP 23: AUTHOR Ismagilov - Flips a block of symbols, returning the new symbol model</summary>
/// <param name="compList"> List of ComponentId's of selected components</param>
/// <param name="model"> Current symbol model</param>
/// <param name="flip"> Type of flip to do</param>
/// <returns>New flipped symbol model</returns>
let flipBlock (compList:ComponentId list) (model:SymbolT.Model) (flip:FlipType) = 
    //Similar structure to rotateBlock, easy to understand
    let SelectedSymbols = List.map (fun x -> model.Symbols |> Map.find x) compList
    let UnselectedSymbols = model.Symbols |> Map.filter (fun x _ -> not (List.contains x compList))
    
    let block = SmartHelpers.getBlock SelectedSymbols
  
    let newSymbols = 
        List.map (fun x -> SmartHelpers.flipSymbolInBlock flip (block.Centre()) x ) SelectedSymbols

    {model with Symbols = 
                ((Map.ofList (List.map2 (fun x y -> (x,y)) compList newSymbols)
                |> Map.fold (fun acc k v -> Map.add k v acc) UnselectedSymbols)
    )}
