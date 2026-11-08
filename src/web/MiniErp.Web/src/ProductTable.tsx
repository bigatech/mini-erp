import React from 'react'
import {
  flexRender,
  getCoreRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type PaginationState,
  type SortingFn,
  type SortingState,
} from '@tanstack/react-table'
import './ProductTable.css'

export type ProductTableRow = {
  id: string
  code: string
  name: string
  type: string
  stockUnit: string
  isActive: boolean
}

type ProductTableProps = {
  products: ProductTableRow[]
  busy: boolean
  filterKey: string
  onEdit: (product: ProductTableRow) => void
  onToggleStatus: (product: ProductTableRow) => void
}

const collator = new Intl.Collator('tr-TR', { numeric: true, sensitivity: 'base' })
const compareProducts: SortingFn<ProductTableRow> = (left, right, columnId) => {
  const a = left.getValue<string | boolean>(columnId)
  const b = right.getValue<string | boolean>(columnId)
  const compared = typeof a === 'boolean' && typeof b === 'boolean'
    ? Number(a) - Number(b)
    : collator.compare(String(a), String(b))
  return compared || collator.compare(left.original.code, right.original.code)
    || left.original.id.localeCompare(right.original.id)
}

export function ProductTable({ products, busy, filterKey, onEdit, onToggleStatus }: ProductTableProps) {
  const [sorting, setSorting] = React.useState<SortingState>([{ id: 'code', desc: false }])
  const [pagination, setPagination] = React.useState<PaginationState>({ pageIndex: 0, pageSize: 10 })
  const columns = React.useMemo<ColumnDef<ProductTableRow>[]>(() => [
    { accessorKey: 'code', header: 'Kod', sortingFn: compareProducts },
    { accessorKey: 'name', header: 'Ürün', sortingFn: compareProducts },
    { accessorKey: 'type', header: 'Tür', sortingFn: compareProducts },
    { accessorKey: 'stockUnit', header: 'Birim', sortingFn: compareProducts },
    { accessorKey: 'isActive', header: 'Durum', sortingFn: compareProducts,
      cell: ({ row }) => row.original.isActive ? 'Aktif' : 'Pasif' },
    { id: 'actions', header: 'İşlem', enableSorting: false,
      cell: ({ row }) => <div className="product-table-actions">
        <button type="button" className="product-refresh" disabled={busy} onClick={() => onEdit(row.original)}>Düzenle</button>
        <button type="button" className="product-refresh" disabled={busy} onClick={() => onToggleStatus(row.original)}>{row.original.isActive ? 'Pasifleştir' : 'Etkinleştir'}</button>
      </div> },
  ], [busy, onEdit, onToggleStatus])

  const table = useReactTable({
    data: products,
    columns,
    getRowId: product => product.id,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    state: { sorting, pagination },
    onSortingChange: setSorting,
    onPaginationChange: setPagination,
    autoResetPageIndex: false,
    enableMultiSort: false,
  })

  React.useEffect(() => {
    setPagination(current => current.pageIndex === 0 ? current : { ...current, pageIndex: 0 })
  }, [filterKey, sorting])

  React.useEffect(() => {
    setPagination(current => {
      const lastPage = Math.max(0, Math.ceil(products.length / current.pageSize) - 1)
      return current.pageIndex <= lastPage ? current : { ...current, pageIndex: lastPage }
    })
  }, [products.length, pagination.pageSize])

  const pageCount = Math.max(1, table.getPageCount())
  const rows = table.getRowModel().rows
  const first = products.length ? pagination.pageIndex * pagination.pageSize + 1 : 0
  const last = products.length ? first + rows.length - 1 : 0

  return <div className="product-table">
    <div className="table-wrap"><table aria-label="Ürün listesi">
      <thead>{table.getHeaderGroups().map(group => <tr key={group.id}>{group.headers.map(header => {
        const sorted = header.column.getIsSorted()
        return <th key={header.id} scope="col" aria-sort={header.column.getCanSort() ? sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : 'none' : undefined}>
          {header.column.getCanSort()
            ? <button type="button" className="product-table-sort" onClick={header.column.getToggleSortingHandler()}>
              {flexRender(header.column.columnDef.header, header.getContext())}<span aria-hidden="true">{sorted === 'asc' ? ' ↑' : sorted === 'desc' ? ' ↓' : ' ↕'}</span>
            </button>
            : flexRender(header.column.columnDef.header, header.getContext())}
        </th>
      })}</tr>)}</thead>
      <tbody>{rows.map(row => <tr key={row.id}>{row.getVisibleCells().map(cell => <td key={cell.id}>{flexRender(cell.column.columnDef.cell, cell.getContext())}</td>)}</tr>)}</tbody>
    </table></div>
    <div className="product-table-pagination" aria-label="Ürün listesi sayfalama">
      <span role="status">{first}–{last} / {products.length} ürün · Sayfa {pagination.pageIndex + 1} / {pageCount}</span>
      <label>Sayfa başına <select aria-label="Sayfa başına ürün" value={pagination.pageSize} onChange={event => table.setPageSize(Number(event.target.value))}>
        {[10, 25, 50].map(size => <option key={size} value={size}>{size}</option>)}
      </select></label>
      <div className="product-table-page-buttons">
        <button type="button" aria-label="İlk sayfa" disabled={busy || !table.getCanPreviousPage()} onClick={() => table.firstPage()}>«</button>
        <button type="button" disabled={busy || !table.getCanPreviousPage()} onClick={() => table.previousPage()}>Önceki</button>
        <button type="button" disabled={busy || !table.getCanNextPage()} onClick={() => table.nextPage()}>Sonraki</button>
        <button type="button" aria-label="Son sayfa" disabled={busy || !table.getCanNextPage()} onClick={() => table.lastPage()}>»</button>
      </div>
    </div>
  </div>
}
