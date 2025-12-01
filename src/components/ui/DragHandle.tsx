import React from 'react'

const DragHandle: React.FC = () => {
  return (
    <div className="draggable-area flex items-center justify-center px-4 py-1 select-none">
      <div className="flex items-center gap-1 opacity-40 hover:opacity-60 transition-opacity">
        <div className="w-1 h-1 rounded-full bg-white/60"></div>
        <div className="w-1 h-1 rounded-full bg-white/60"></div>
        <div className="w-1 h-1 rounded-full bg-white/60"></div>
        <div className="w-1 h-1 rounded-full bg-white/60"></div>
        <div className="w-1 h-1 rounded-full bg-white/60"></div>
        <div className="w-1 h-1 rounded-full bg-white/60"></div>
      </div>
    </div>
  )
}

export default DragHandle
