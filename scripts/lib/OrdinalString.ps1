# Script-Governance:
#   Category: library
#   SideEffects:
#     - None
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

function Get-NervStringCompositeKey {
    <#
        Encodes a string sequence into an injective, prefix-decodable ordinal key.

        Non-null strings escape backslash and the component separator. An unescaped `\n` token
        represents null, while `\z` represents the zero-component sequence; literal backslashes are
        always doubled, so neither marker can collide with string content. Empty strings remain empty
        component tokens, and separators preserve both arity and trailing empty components. Keys for
        existing inputs with at least one component retain their historical bytes and ordinal ordering
        when every component is non-empty, non-null, and contains neither backslash nor the separator.
        A directly-bound null argument is one null component, exactly like @($null); it is not the
        zero-component sequence represented by an explicitly empty array.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [object[]] $Components)

    # Array-subexpression preserves the binder's direct-null element while keeping @() empty.
    [object[]] $componentValues = @($Components)
    if ($componentValues.Count -eq 0) {
        return '\z'
    }

    return (@($componentValues | ForEach-Object {
        if ($null -eq $_) { return '\n' }
        if ($_ -isnot [string]) { throw "Composite key components must be strings or null; received '$($_.GetType().FullName)'." }
        $_.Replace('\', '\\').Replace('|', '\|')
    }) -join '|')
}

function Get-NervStringSet {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [AllowNull()] [string[]] $Values,
        [Parameter(Mandatory)] [System.StringComparer] $Comparer
    )

    return ,([Collections.Generic.HashSet[string]]::new([string[]]@($Values), $Comparer))
}

function Get-NervStringsSorted {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [AllowNull()] [string[]] $Values,
        [Parameter(Mandatory)] [System.StringComparer] $Comparer,
        [switch] $Unique
    )

    $items = [Collections.Generic.List[string]]::new()
    if ($Unique) {
        $items.AddRange([Collections.Generic.HashSet[string]]::new([string[]]@($Values), $Comparer))
    }
    else {
        $items.AddRange([string[]]@($Values))
    }
    $items.Sort($Comparer)
    return @($items)
}

function Get-NervStringsUniqueInOrder {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [AllowNull()] [string[]] $Values,
        [Parameter(Mandatory)] [System.StringComparer] $Comparer
    )

    $seen = [Collections.Generic.HashSet[string]]::new($Comparer)
    return @(foreach ($value in @($Values)) { if ($seen.Add($value)) { $value } })
}

function Get-NervStringGroups {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [object[]] $Items,
        [Parameter(Mandatory)] [scriptblock] $KeySelector,
        [Parameter(Mandatory)] [System.StringComparer] $Comparer
    )

    $groups = [Collections.Generic.Dictionary[string, Collections.Generic.List[object]]]::new($Comparer)
    foreach ($item in @($Items)) {
        if ($null -eq $item) { continue }
        $key = [string](& $KeySelector $item)
        if (-not $groups.ContainsKey($key)) { $groups[$key] = [Collections.Generic.List[object]]::new() }
        $groups[$key].Add($item)
    }

    return @(Get-NervStringsSorted -Values @($groups.Keys) -Comparer $Comparer | ForEach-Object {
        [pscustomobject]@{ Name = $_; Count = $groups[$_].Count; Group = @($groups[$_]) }
    })
}

function Get-NervItemsSortedByString {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [object[]] $Items,
        [Parameter(Mandatory)] [scriptblock] $KeySelector,
        [Parameter(Mandatory)] [System.StringComparer] $Comparer
    )

    return @(Get-NervStringGroups -Items $Items -KeySelector $KeySelector -Comparer $Comparer |
        ForEach-Object { @($_.Group) })
}

function Get-NervItemsUniqueSortedByString {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [object[]] $Items,
        [Parameter(Mandatory)] [scriptblock] $KeySelector,
        [Parameter(Mandatory)] [System.StringComparer] $Comparer
    )

    return @(Get-NervStringGroups -Items $Items -KeySelector $KeySelector -Comparer $Comparer |
        ForEach-Object { ,@($_.Group)[0] })
}

function Get-NervItemsSorted {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [object[]] $Items,
        [Parameter(Mandatory)] [Comparison[object]] $Comparison
    )

    $list = [Collections.Generic.List[object]]::new()
    $list.AddRange([object[]]@($Items))
    $list.Sort($Comparison)
    return @($list)
}
